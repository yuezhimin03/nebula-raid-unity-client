#include "nebula_native.h"

#include <algorithm>
#include <cstdint>
#include <limits>
#include <memory>
#include <new>
#include <type_traits>
#include <vector>

namespace {

constexpr uint32_t kAbiVersion = 1;
constexpr uint32_t kMaximumCapacity = 1'000'000;
constexpr int32_t kMaximumArenaHalfExtent = 1'000'000;
constexpr int64_t kMaximumGridCells = 4'194'304;
constexpr uint64_t kFnvOffset = 14695981039346656037ULL;
constexpr uint64_t kFnvPrime = 1099511628211ULL;

static_assert(sizeof(NebulaNativeWorldConfig) == 12U, "C ABI config layout drift");
static_assert(sizeof(NebulaNativeActorSpawn) == 32U, "C ABI spawn layout drift");
static_assert(sizeof(NebulaNativeCommand) == 20U, "C ABI command layout drift");
static_assert(sizeof(NebulaNativeActorState) == 28U, "C ABI state layout drift");
static_assert(sizeof(NebulaNativeWorldStats) == 32U, "C ABI stats layout drift");

template <typename T>
void hash_integer(uint64_t& hash, T value) noexcept {
    using Unsigned = typename std::make_unsigned<T>::type;
    Unsigned bits = static_cast<Unsigned>(value);
    for (size_t index = 0; index < sizeof(T); ++index) {
        hash ^= static_cast<uint8_t>(bits & static_cast<Unsigned>(0xFFU));
        hash *= kFnvPrime;
        bits >>= 8U;
    }
}

int32_t clamp_position(int64_t value, int32_t half_extent) noexcept {
    return static_cast<int32_t>(std::max(
        -static_cast<int64_t>(half_extent),
        std::min(static_cast<int64_t>(half_extent), value)));
}

bool is_valid_config(const NebulaNativeWorldConfig& config) noexcept {
    if (config.capacity == 0U || config.capacity > kMaximumCapacity) {
        return false;
    }
    if (config.arena_half_extent_mm <= 0
        || config.arena_half_extent_mm > kMaximumArenaHalfExtent
        || config.grid_cell_size_mm <= 0) {
        return false;
    }
    const int64_t span = static_cast<int64_t>(config.arena_half_extent_mm) * 2;
    if (config.grid_cell_size_mm > span) {
        return false;
    }
    const int64_t dimension = (span / config.grid_cell_size_mm) + 1;
    return dimension > 0 && dimension * dimension <= kMaximumGridCells;
}

bool is_valid_spawn(
    const NebulaNativeWorldConfig& config,
    const NebulaNativeActorSpawn& spawn) noexcept {
    return spawn.team != 0U
        && spawn.position_x_mm >= -config.arena_half_extent_mm
        && spawn.position_x_mm <= config.arena_half_extent_mm
        && spawn.position_y_mm >= -config.arena_half_extent_mm
        && spawn.position_y_mm <= config.arena_half_extent_mm
        && spawn.health > 0
        && spawn.speed_mm_per_tick >= 0
        && spawn.damage > 0
        && spawn.attack_range_mm > 0
        && spawn.attack_range_mm <= config.arena_half_extent_mm * 2
        && spawn.attack_cooldown_ticks > 0U;
}

} // namespace

struct NebulaNativeWorld {
    explicit NebulaNativeWorld(const NebulaNativeWorldConfig& value)
        : config(value),
          grid_dimension(
              static_cast<int32_t>(
                  (static_cast<int64_t>(value.arena_half_extent_mm) * 2
                      / value.grid_cell_size_mm)
                  + 1)),
          alive(value.capacity, 0U),
          team(value.capacity, 0U),
          position_x(value.capacity, 0),
          position_y(value.capacity, 0),
          health(value.capacity, 0),
          speed(value.capacity, 0),
          damage(value.capacity, 0),
          attack_range(value.capacity, 0),
          cooldown(value.capacity, 0U),
          cooldown_period(value.capacity, 0U),
          pending_damage(value.capacity, 0),
          attack_requested(value.capacity, 0U),
          next_in_cell(value.capacity, -1),
          cell_head(
              static_cast<size_t>(grid_dimension)
                  * static_cast<size_t>(grid_dimension),
              -1) {}

    bool validate_commands(
        const NebulaNativeCommand* commands,
        size_t command_count) const noexcept {
        if (command_count > 0U && commands == nullptr) {
            return false;
        }
        if (command_count > actor_count) {
            return false;
        }
        uint32_t previous_actor = 0U;
        for (size_t index = 0; index < command_count; ++index) {
            const NebulaNativeCommand& command = commands[index];
            if (command.tick != tick
                || command.actor_id >= actor_count
                || alive[command.actor_id] == 0U
                || command.move_x < -1
                || command.move_x > 1
                || command.move_y < -1
                || command.move_y > 1
                || (command.ability & ~static_cast<uint32_t>(
                        NEBULA_NATIVE_ABILITY_PRIMARY)) != 0U
                || (index > 0U && command.actor_id <= previous_actor)) {
                return false;
            }
            previous_actor = command.actor_id;
        }
        return true;
    }

    int32_t cell_coordinate(int32_t position) const noexcept {
        const int64_t shifted =
            static_cast<int64_t>(position) + config.arena_half_extent_mm;
        const int64_t coordinate = shifted / config.grid_cell_size_mm;
        return static_cast<int32_t>(std::max(
            int64_t{0},
            std::min<int64_t>(grid_dimension - 1, coordinate)));
    }

    size_t cell_index(int32_t cell_x, int32_t cell_y) const noexcept {
        return static_cast<size_t>(cell_y)
            * static_cast<size_t>(grid_dimension)
            + static_cast<size_t>(cell_x);
    }

    void rebuild_grid() noexcept {
        std::fill(cell_head.begin(), cell_head.end(), -1);
        std::fill(next_in_cell.begin(), next_in_cell.end(), -1);
        for (uint32_t actor = actor_count; actor > 0U; --actor) {
            const uint32_t actor_id = actor - 1U;
            if (alive[actor_id] == 0U) {
                continue;
            }
            const int32_t cell_x = cell_coordinate(position_x[actor_id]);
            const int32_t cell_y = cell_coordinate(position_y[actor_id]);
            const size_t index = cell_index(cell_x, cell_y);
            next_in_cell[actor_id] = cell_head[index];
            cell_head[index] = static_cast<int32_t>(actor_id);
        }
    }

    int32_t find_target(uint32_t actor_id) const noexcept {
        const int32_t range = attack_range[actor_id];
        const int64_t range_squared =
            static_cast<int64_t>(range) * static_cast<int64_t>(range);
        const int32_t origin_x = cell_coordinate(position_x[actor_id]);
        const int32_t origin_y = cell_coordinate(position_y[actor_id]);
        const int32_t cell_radius =
            (range + config.grid_cell_size_mm - 1)
            / config.grid_cell_size_mm;
        const int32_t minimum_x = std::max(0, origin_x - cell_radius);
        const int32_t maximum_x =
            std::min(grid_dimension - 1, origin_x + cell_radius);
        const int32_t minimum_y = std::max(0, origin_y - cell_radius);
        const int32_t maximum_y =
            std::min(grid_dimension - 1, origin_y + cell_radius);

        int32_t selected = -1;
        int64_t selected_distance = std::numeric_limits<int64_t>::max();
        for (int32_t cell_y = minimum_y; cell_y <= maximum_y; ++cell_y) {
            for (int32_t cell_x = minimum_x; cell_x <= maximum_x; ++cell_x) {
                int32_t candidate = cell_head[cell_index(cell_x, cell_y)];
                while (candidate >= 0) {
                    const uint32_t candidate_id =
                        static_cast<uint32_t>(candidate);
                    if (alive[candidate_id] != 0U
                        && team[candidate_id] != team[actor_id]) {
                        const int64_t delta_x =
                            static_cast<int64_t>(position_x[candidate_id])
                            - position_x[actor_id];
                        const int64_t delta_y =
                            static_cast<int64_t>(position_y[candidate_id])
                            - position_y[actor_id];
                        const int64_t distance =
                            delta_x * delta_x + delta_y * delta_y;
                        if (distance <= range_squared
                            && (distance < selected_distance
                                || (distance == selected_distance
                                    && candidate < selected))) {
                            selected = candidate;
                            selected_distance = distance;
                        }
                    }
                    candidate = next_in_cell[candidate_id];
                }
            }
        }
        return selected;
    }

    uint64_t checksum() const noexcept {
        uint64_t hash = kFnvOffset;
        hash_integer(hash, tick);
        hash_integer(hash, actor_count);
        for (uint32_t actor_id = 0; actor_id < actor_count; ++actor_id) {
            hash_integer(hash, actor_id);
            hash_integer(hash, alive[actor_id]);
            hash_integer(hash, team[actor_id]);
            hash_integer(hash, position_x[actor_id]);
            hash_integer(hash, position_y[actor_id]);
            hash_integer(hash, health[actor_id]);
            hash_integer(hash, cooldown[actor_id]);
        }
        return hash;
    }

    NebulaNativeWorldConfig config;
    int32_t grid_dimension;
    uint32_t actor_count = 0U;
    uint32_t tick = 0U;
    uint64_t attacks_resolved = 0U;

    // Structure-of-arrays authoritative state. Every buffer is sized once in
    // the constructor; step() only fills/reuses storage and never grows it.
    std::vector<uint8_t> alive;
    std::vector<uint32_t> team;
    std::vector<int32_t> position_x;
    std::vector<int32_t> position_y;
    std::vector<int32_t> health;
    std::vector<int32_t> speed;
    std::vector<int32_t> damage;
    std::vector<int32_t> attack_range;
    std::vector<uint32_t> cooldown;
    std::vector<uint32_t> cooldown_period;

    // Preallocated scratch state for simultaneous damage and the intrusive
    // uniform-grid linked lists.
    std::vector<int64_t> pending_damage;
    std::vector<uint8_t> attack_requested;
    std::vector<int32_t> next_in_cell;
    std::vector<int32_t> cell_head;
};

extern "C" {

uint32_t NEBULA_NATIVE_CALL nebula_native_abi_version(void) {
    return kAbiVersion;
}

int32_t NEBULA_NATIVE_CALL nebula_world_create(
    const NebulaNativeWorldConfig* config,
    NebulaNativeWorld** out_world) {
    if (out_world == nullptr) {
        return NEBULA_NATIVE_INVALID_ARGUMENT;
    }
    *out_world = nullptr;
    if (config == nullptr || !is_valid_config(*config)) {
        return NEBULA_NATIVE_INVALID_ARGUMENT;
    }
    try {
        std::unique_ptr<NebulaNativeWorld> world(
            new NebulaNativeWorld(*config));
        *out_world = world.release();
        return NEBULA_NATIVE_OK;
    } catch (const std::bad_alloc&) {
        return NEBULA_NATIVE_OUT_OF_MEMORY;
    } catch (...) {
        return NEBULA_NATIVE_INTERNAL_ERROR;
    }
}

void NEBULA_NATIVE_CALL nebula_world_destroy(NebulaNativeWorld* world) {
    delete world;
}

int32_t NEBULA_NATIVE_CALL nebula_world_spawn(
    NebulaNativeWorld* world,
    const NebulaNativeActorSpawn* spawn,
    uint32_t* out_actor_id) {
    if (world == nullptr || spawn == nullptr || out_actor_id == nullptr) {
        return NEBULA_NATIVE_INVALID_ARGUMENT;
    }
    if (!is_valid_spawn(world->config, *spawn)) {
        return NEBULA_NATIVE_INVALID_ARGUMENT;
    }
    if (world->actor_count >= world->config.capacity) {
        return NEBULA_NATIVE_CAPACITY_EXCEEDED;
    }

    const uint32_t actor_id = world->actor_count++;
    world->alive[actor_id] = 1U;
    world->team[actor_id] = spawn->team;
    world->position_x[actor_id] = spawn->position_x_mm;
    world->position_y[actor_id] = spawn->position_y_mm;
    world->health[actor_id] = spawn->health;
    world->speed[actor_id] = spawn->speed_mm_per_tick;
    world->damage[actor_id] = spawn->damage;
    world->attack_range[actor_id] = spawn->attack_range_mm;
    world->cooldown[actor_id] = 0U;
    world->cooldown_period[actor_id] = spawn->attack_cooldown_ticks;
    *out_actor_id = actor_id;
    return NEBULA_NATIVE_OK;
}

int32_t NEBULA_NATIVE_CALL nebula_world_step(
    NebulaNativeWorld* world,
    const NebulaNativeCommand* commands,
    size_t command_count) {
    if (world == nullptr) {
        return NEBULA_NATIVE_INVALID_ARGUMENT;
    }
    if (!world->validate_commands(commands, command_count)) {
        return NEBULA_NATIVE_NONCANONICAL_COMMANDS;
    }

    std::fill(world->pending_damage.begin(), world->pending_damage.end(), 0);
    std::fill(
        world->attack_requested.begin(),
        world->attack_requested.end(),
        uint8_t{0});
    for (uint32_t actor_id = 0; actor_id < world->actor_count; ++actor_id) {
        if (world->alive[actor_id] != 0U && world->cooldown[actor_id] > 0U) {
            --world->cooldown[actor_id];
        }
    }

    for (size_t index = 0; index < command_count; ++index) {
        const NebulaNativeCommand& command = commands[index];
        const uint32_t actor_id = command.actor_id;
        world->position_x[actor_id] = clamp_position(
            static_cast<int64_t>(world->position_x[actor_id])
                + static_cast<int64_t>(world->speed[actor_id])
                    * command.move_x,
            world->config.arena_half_extent_mm);
        world->position_y[actor_id] = clamp_position(
            static_cast<int64_t>(world->position_y[actor_id])
                + static_cast<int64_t>(world->speed[actor_id])
                    * command.move_y,
            world->config.arena_half_extent_mm);
        world->attack_requested[actor_id] =
            static_cast<uint8_t>(
                (command.ability & NEBULA_NATIVE_ABILITY_PRIMARY) != 0U);
    }

    world->rebuild_grid();
    for (uint32_t actor_id = 0; actor_id < world->actor_count; ++actor_id) {
        if (world->alive[actor_id] == 0U
            || world->attack_requested[actor_id] == 0U
            || world->cooldown[actor_id] != 0U) {
            continue;
        }
        const int32_t target = world->find_target(actor_id);
        if (target < 0) {
            continue;
        }
        const uint32_t target_id = static_cast<uint32_t>(target);
        world->pending_damage[target_id] += world->damage[actor_id];
        world->cooldown[actor_id] = world->cooldown_period[actor_id];
        ++world->attacks_resolved;
    }

    for (uint32_t actor_id = 0; actor_id < world->actor_count; ++actor_id) {
        if (world->alive[actor_id] == 0U
            || world->pending_damage[actor_id] <= 0) {
            continue;
        }
        if (world->pending_damage[actor_id] >= world->health[actor_id]) {
            world->health[actor_id] = 0;
            world->alive[actor_id] = 0U;
        } else {
            world->health[actor_id] -=
                static_cast<int32_t>(world->pending_damage[actor_id]);
        }
    }
    ++world->tick;
    return NEBULA_NATIVE_OK;
}

int32_t NEBULA_NATIVE_CALL nebula_world_get_actor(
    const NebulaNativeWorld* world,
    uint32_t actor_id,
    NebulaNativeActorState* out_state) {
    if (world == nullptr
        || out_state == nullptr
        || actor_id >= world->actor_count) {
        return NEBULA_NATIVE_INVALID_ARGUMENT;
    }
    out_state->actor_id = actor_id;
    out_state->team = world->team[actor_id];
    out_state->position_x_mm = world->position_x[actor_id];
    out_state->position_y_mm = world->position_y[actor_id];
    out_state->health = world->health[actor_id];
    out_state->alive = world->alive[actor_id];
    out_state->cooldown_ticks = world->cooldown[actor_id];
    return NEBULA_NATIVE_OK;
}

int32_t NEBULA_NATIVE_CALL nebula_world_get_stats(
    const NebulaNativeWorld* world,
    NebulaNativeWorldStats* out_stats) {
    if (world == nullptr || out_stats == nullptr) {
        return NEBULA_NATIVE_INVALID_ARGUMENT;
    }
    uint32_t alive_count = 0U;
    for (uint32_t actor_id = 0; actor_id < world->actor_count; ++actor_id) {
        alive_count += world->alive[actor_id] != 0U ? 1U : 0U;
    }
    out_stats->tick = world->tick;
    out_stats->actor_count = world->actor_count;
    out_stats->alive_count = alive_count;
    out_stats->reserved = 0U;
    out_stats->attacks_resolved = world->attacks_resolved;
    out_stats->checksum = world->checksum();
    return NEBULA_NATIVE_OK;
}

} // extern "C"
