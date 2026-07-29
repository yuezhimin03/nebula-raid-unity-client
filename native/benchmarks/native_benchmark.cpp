#include "nebula_native.h"

#include <algorithm>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <cstdlib>
#include <iomanip>
#include <iostream>
#include <limits>
#include <stdexcept>
#include <string>
#include <vector>

namespace {

struct Options {
    uint32_t entities = 4'096U;
    uint32_t ticks = 500U;
};

uint32_t parse_positive(const char* value, const char* label) {
    const std::string text(value);
    size_t consumed = 0U;
    const unsigned long parsed = std::stoul(text, &consumed, 10);
    if (consumed != text.size()
        || parsed == 0UL
        || parsed > std::numeric_limits<uint32_t>::max()) {
        throw std::invalid_argument(std::string(label) + " is invalid");
    }
    return static_cast<uint32_t>(parsed);
}
Options parse_options(int argc, char** argv) {
    Options options;
    for (int index = 1; index < argc; ++index) {
        const std::string argument(argv[index]);
        if (argument == "--entities" && index + 1 < argc) {
            options.entities = parse_positive(argv[++index], "--entities");
        } else if (argument == "--ticks" && index + 1 < argc) {
            options.ticks = parse_positive(argv[++index], "--ticks");
        } else {
            throw std::invalid_argument(
                "usage: --entities EVEN_NUMBER --ticks POSITIVE_NUMBER");
        }
    }
    if (options.entities < 2U
        || options.entities > 100'000U
        || options.entities % 2U != 0U) {
        throw std::invalid_argument(
            "--entities must be even and between 2 and 100000");
    }
    if (options.ticks > 100'000U) {
        throw std::invalid_argument("--ticks must be at most 100000");
    }
    return options;
}

void check(int32_t status, const char* operation) {
    if (status != NEBULA_NATIVE_OK) {
        throw std::runtime_error(
            std::string(operation) + " failed with status "
            + std::to_string(status));
    }
}

} // namespace

int main(int argc, char** argv) {
    try {
        const Options options = parse_options(argc, argv);
        constexpr uint32_t warmup_ticks = 20U;
        const uint32_t pair_count = options.entities / 2U;
        const int32_t columns =
            static_cast<int32_t>(std::ceil(std::sqrt(pair_count)));
        const int32_t spacing = 2'500;
        const int32_t half_extent =
            std::max(10'000, (columns * spacing / 2) + 3'000);
        const NebulaNativeWorldConfig config{
            options.entities,
            half_extent,
            1'000,
        };
        NebulaNativeWorld* raw_world = nullptr;
        check(nebula_world_create(&config, &raw_world), "create");
        struct WorldGuard {
            NebulaNativeWorld* value;
            ~WorldGuard() {
                nebula_world_destroy(value);
            }
        } world{raw_world};

        for (uint32_t pair = 0U; pair < pair_count; ++pair) {
            const int32_t column = static_cast<int32_t>(pair) % columns;
            const int32_t row = static_cast<int32_t>(pair) / columns;
            const int32_t x =
                (column * spacing) - ((columns - 1) * spacing / 2);
            const int32_t y =
                (row * spacing) - ((columns - 1) * spacing / 2);
            const NebulaNativeActorSpawn actors[] = {
                {1U, x - 300, y, 1'000'000'000, 0, 1, 1'000, 1U},
                {2U, x + 300, y, 1'000'000'000, 0, 1, 1'000, 1U},
            };
            for (const NebulaNativeActorSpawn& actor : actors) {
                uint32_t actor_id = 0U;
                check(
                    nebula_world_spawn(world.value, &actor, &actor_id),
                    "spawn");
            }
        }

        std::vector<NebulaNativeCommand> commands(options.entities);
        const auto prepare_commands = [&](uint32_t tick) {
            for (uint32_t actor_id = 0U;
                 actor_id < options.entities;
                 ++actor_id) {
                commands[actor_id] = NebulaNativeCommand{
                    tick,
                    actor_id,
                    0,
                    0,
                    NEBULA_NATIVE_ABILITY_PRIMARY,
                };
            }
        };

        for (uint32_t tick = 0U; tick < warmup_ticks; ++tick) {
            prepare_commands(tick);
            check(
                nebula_world_step(
                    world.value,
                    commands.data(),
                    commands.size()),
                "warmup step");
        }

        const auto started = std::chrono::steady_clock::now();
        for (uint32_t measured = 0U; measured < options.ticks; ++measured) {
            prepare_commands(warmup_ticks + measured);
            check(
                nebula_world_step(
                    world.value,
                    commands.data(),
                    commands.size()),
                "measured step");
        }
        const auto stopped = std::chrono::steady_clock::now();
        const double elapsed_ms =
            std::chrono::duration<double, std::milli>(stopped - started).count();
        const double ticks_per_second =
            static_cast<double>(options.ticks) * 1'000.0 / elapsed_ms;
        const double actor_steps_per_second =
            ticks_per_second * options.entities;
        NebulaNativeWorldStats result{};
        check(nebula_world_get_stats(world.value, &result), "get stats");

        std::cout << "Nebula Native C ABI microbenchmark"
                  << " (simulation step only; not a Unity Player benchmark)\n";
        std::cout << "abiVersion=" << nebula_native_abi_version()
                  << "; entities=" << options.entities
                  << "; warmupTicks=" << warmup_ticks
                  << "; measuredTicks=" << options.ticks << '\n';
        std::cout << std::fixed << std::setprecision(3)
                  << "elapsedMs=" << elapsed_ms
                  << "; ticksPerSecond=" << std::setprecision(1)
                  << ticks_per_second
                  << "; actorStepsPerSecond=" << std::setprecision(0)
                  << actor_steps_per_second << '\n';
        std::cout << "attacksResolved=" << result.attacks_resolved
                  << "; alive=" << result.alive_count
                  << "; checksum=0x" << std::hex << std::uppercase
                  << result.checksum << std::dec << '\n';
        std::cout
            << "scope: commands and all SoA/grid scratch buffers are preallocated;"
            << " timing excludes create/spawn and command allocation.\n";
        return EXIT_SUCCESS;
    } catch (const std::exception& exception) {
        std::cerr << exception.what() << '\n';
        return EXIT_FAILURE;
    }
}
