#include "nebula_native.h"

#include <cstdint>
#include <cstdlib>
#include <iostream>
#include <iterator>
#include <stdexcept>
#include <string>
#include <vector>

namespace {

void require(bool condition, const std::string& message) {
    if (!condition) {
        throw std::runtime_error(message);
    }
}

void require_status(int32_t actual, int32_t expected, const char* operation) {
    if (actual != expected) {
        throw std::runtime_error(
            std::string(operation) + ": expected status "
            + std::to_string(expected) + ", got " + std::to_string(actual));
    }
}

class World final {
public:
    explicit World(const NebulaNativeWorldConfig& config) {
        require_status(
            nebula_world_create(&config, &value_),
            NEBULA_NATIVE_OK,
            "create");
    }

    ~World() {
        nebula_world_destroy(value_);
    }

    World(const World&) = delete;
    World& operator=(const World&) = delete;

    NebulaNativeWorld* get() const noexcept {
        return value_;
    }

private:
    NebulaNativeWorld* value_ = nullptr;
};

uint32_t spawn(
    World& world,
    uint32_t team,
    int32_t x,
    int32_t y,
    int32_t health = 10,
    int32_t damage = 10) {
    const NebulaNativeActorSpawn spec{
        team,
        x,
        y,
        health,
        25,
        damage,
        1'000,
        1U,
    };
    uint32_t actor_id = UINT32_MAX;
    require_status(
        nebula_world_spawn(world.get(), &spec, &actor_id),
        NEBULA_NATIVE_OK,
        "spawn");
    return actor_id;
}

NebulaNativeWorldStats stats(const World& world) {
    NebulaNativeWorldStats value{};
    require_status(
        nebula_world_get_stats(world.get(), &value),
        NEBULA_NATIVE_OK,
        "get stats");
    return value;
}

void test_abi_and_argument_validation() {
    require(nebula_native_abi_version() == 1U, "ABI version must be 1");
    NebulaNativeWorld* invalid_world = nullptr;
    const NebulaNativeWorldConfig invalid{0U, 10'000, 1'000};
    require_status(
        nebula_world_create(&invalid, &invalid_world),
        NEBULA_NATIVE_INVALID_ARGUMENT,
        "reject zero capacity");
    require(invalid_world == nullptr, "failed create must clear output");
    nebula_world_destroy(nullptr);
}

void test_simultaneous_damage_and_raii() {
    const NebulaNativeWorldConfig config{2U, 10'000, 1'000};
    World world(config);
    const uint32_t first = spawn(world, 1U, -100, 0);
    const uint32_t second = spawn(world, 2U, 100, 0);
    require(first == 0U && second == 1U, "actor ids must be stable");

    const NebulaNativeCommand commands[] = {
        {0U, first, 0, 0, NEBULA_NATIVE_ABILITY_PRIMARY},
        {0U, second, 0, 0, NEBULA_NATIVE_ABILITY_PRIMARY},
    };
    require_status(
        nebula_world_step(world.get(), commands, 2U),
        NEBULA_NATIVE_OK,
        "simultaneous step");

    const NebulaNativeWorldStats result = stats(world);
    require(result.tick == 1U, "tick must advance once");
    require(result.actor_count == 2U, "actor count");
    require(result.alive_count == 0U, "both actors must die simultaneously");
    require(result.attacks_resolved == 2U, "both planned attacks must resolve");

    NebulaNativeActorState actor{};
    require_status(
        nebula_world_get_actor(world.get(), first, &actor),
        NEBULA_NATIVE_OK,
        "get actor");
    require(actor.alive == 0U && actor.health == 0, "first actor death state");
}

uint64_t run_deterministic_scenario() {
    constexpr uint32_t actor_count = 128U;
    constexpr uint32_t measured_ticks = 120U;
    const NebulaNativeWorldConfig config{actor_count, 40'000, 1'000};
    World world(config);
    for (uint32_t pair = 0U; pair < actor_count / 2U; ++pair) {
        const int32_t column = static_cast<int32_t>(pair % 8U);
        const int32_t row = static_cast<int32_t>(pair / 8U);
        const int32_t x = (column - 4) * 4'000;
        const int32_t y = (row - 4) * 4'000;
        spawn(world, 1U, x - 300, y, 1'000'000, 3);
        spawn(world, 2U, x + 300, y, 1'000'000, 3);
    }

    std::vector<NebulaNativeCommand> commands(actor_count);
    for (uint32_t tick = 0U; tick < measured_ticks; ++tick) {
        for (uint32_t actor_id = 0U; actor_id < actor_count; ++actor_id) {
            commands[actor_id] = NebulaNativeCommand{
                tick,
                actor_id,
                static_cast<int32_t>((tick + actor_id) % 3U) - 1,
                0,
                NEBULA_NATIVE_ABILITY_PRIMARY,
            };
        }
        require_status(
            nebula_world_step(world.get(), commands.data(), commands.size()),
            NEBULA_NATIVE_OK,
            "deterministic step");
    }
    return stats(world).checksum;
}

void test_determinism_and_transactional_rejection() {
    const uint64_t first = run_deterministic_scenario();
    const uint64_t second = run_deterministic_scenario();
    require(first == second, "same scenario must produce the same checksum");

    const NebulaNativeWorldConfig config{2U, 10'000, 1'000};
    World world(config);
    spawn(world, 1U, -100, 0, 100, 1);
    spawn(world, 2U, 100, 0, 100, 1);
    const NebulaNativeWorldStats before = stats(world);
    const NebulaNativeCommand duplicate[] = {
        {0U, 0U, 1, 0, NEBULA_NATIVE_ABILITY_PRIMARY},
        {0U, 0U, 0, 0, NEBULA_NATIVE_ABILITY_PRIMARY},
    };
    require_status(
        nebula_world_step(world.get(), duplicate, 2U),
        NEBULA_NATIVE_NONCANONICAL_COMMANDS,
        "reject duplicate command");
    const NebulaNativeWorldStats after = stats(world);
    require(after.tick == before.tick, "rejected frame must not advance tick");
    require(
        after.checksum == before.checksum,
        "rejected frame must not mutate authoritative state");
}

void test_capacity_boundary() {
    const NebulaNativeWorldConfig config{1U, 10'000, 1'000};
    World world(config);
    spawn(world, 1U, 0, 0);
    const NebulaNativeActorSpawn extra{
        2U,
        100,
        0,
        10,
        0,
        1,
        1'000,
        1U,
    };
    uint32_t actor_id = 0U;
    require_status(
        nebula_world_spawn(world.get(), &extra, &actor_id),
        NEBULA_NATIVE_CAPACITY_EXCEEDED,
        "capacity boundary");
}

} // namespace

int main() {
    struct TestCase {
        const char* name;
        void (*body)();
    };
    const TestCase tests[] = {
        {"abi/argument-validation", test_abi_and_argument_validation},
        {"combat/simultaneous-damage-raii", test_simultaneous_damage_and_raii},
        {"combat/determinism-transactional-rejection",
         test_determinism_and_transactional_rejection},
        {"storage/capacity-boundary", test_capacity_boundary},
    };

    size_t passed = 0U;
    for (const TestCase& test : tests) {
        try {
            test.body();
            ++passed;
            std::cout << "[PASS] " << test.name << '\n';
        } catch (const std::exception& exception) {
            std::cerr << "[FAIL] " << test.name << ": "
                      << exception.what() << '\n';
        }
    }
    std::cout << "summary: total=" << std::size(tests)
              << ", passed=" << passed
              << ", failed=" << (std::size(tests) - passed) << '\n';
    return passed == std::size(tests) ? EXIT_SUCCESS : EXIT_FAILURE;
}
