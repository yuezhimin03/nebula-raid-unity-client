#pragma once

#include <stddef.h>
#include <stdint.h>

#if defined(_WIN32)
#if defined(NEBULA_NATIVE_EXPORTS)
#define NEBULA_NATIVE_API __declspec(dllexport)
#else
#define NEBULA_NATIVE_API __declspec(dllimport)
#endif
#define NEBULA_NATIVE_CALL __cdecl
#else
#define NEBULA_NATIVE_API __attribute__((visibility("default")))
#define NEBULA_NATIVE_CALL
#endif

#ifdef __cplusplus
extern "C" {
#endif

enum NebulaNativeStatus {
    NEBULA_NATIVE_OK = 0,
    NEBULA_NATIVE_INVALID_ARGUMENT = 1,
    NEBULA_NATIVE_CAPACITY_EXCEEDED = 2,
    NEBULA_NATIVE_NONCANONICAL_COMMANDS = 3,
    NEBULA_NATIVE_OUT_OF_MEMORY = 4,
    NEBULA_NATIVE_INTERNAL_ERROR = 5
};

enum NebulaNativeAbility {
    NEBULA_NATIVE_ABILITY_NONE = 0,
    NEBULA_NATIVE_ABILITY_PRIMARY = 1
};

typedef struct NebulaNativeWorld NebulaNativeWorld;

typedef struct NebulaNativeWorldConfig {
    uint32_t capacity;
    int32_t arena_half_extent_mm;
    int32_t grid_cell_size_mm;
} NebulaNativeWorldConfig;

typedef struct NebulaNativeActorSpawn {
    uint32_t team;
    int32_t position_x_mm;
    int32_t position_y_mm;
    int32_t health;
    int32_t speed_mm_per_tick;
    int32_t damage;
    int32_t attack_range_mm;
    uint32_t attack_cooldown_ticks;
} NebulaNativeActorSpawn;

typedef struct NebulaNativeCommand {
    uint32_t tick;
    uint32_t actor_id;
    int32_t move_x;
    int32_t move_y;
    uint32_t ability;
} NebulaNativeCommand;

typedef struct NebulaNativeActorState {
    uint32_t actor_id;
    uint32_t team;
    int32_t position_x_mm;
    int32_t position_y_mm;
    int32_t health;
    uint32_t alive;
    uint32_t cooldown_ticks;
} NebulaNativeActorState;

typedef struct NebulaNativeWorldStats {
    uint32_t tick;
    uint32_t actor_count;
    uint32_t alive_count;
    uint32_t reserved;
    uint64_t attacks_resolved;
    uint64_t checksum;
} NebulaNativeWorldStats;

NEBULA_NATIVE_API uint32_t NEBULA_NATIVE_CALL nebula_native_abi_version(void);

NEBULA_NATIVE_API int32_t NEBULA_NATIVE_CALL nebula_world_create(
    const NebulaNativeWorldConfig* config,
    NebulaNativeWorld** out_world);

NEBULA_NATIVE_API void NEBULA_NATIVE_CALL nebula_world_destroy(
    NebulaNativeWorld* world);

NEBULA_NATIVE_API int32_t NEBULA_NATIVE_CALL nebula_world_spawn(
    NebulaNativeWorld* world,
    const NebulaNativeActorSpawn* spawn,
    uint32_t* out_actor_id);

NEBULA_NATIVE_API int32_t NEBULA_NATIVE_CALL nebula_world_step(
    NebulaNativeWorld* world,
    const NebulaNativeCommand* commands,
    size_t command_count);

NEBULA_NATIVE_API int32_t NEBULA_NATIVE_CALL nebula_world_get_actor(
    const NebulaNativeWorld* world,
    uint32_t actor_id,
    NebulaNativeActorState* out_state);

NEBULA_NATIVE_API int32_t NEBULA_NATIVE_CALL nebula_world_get_stats(
    const NebulaNativeWorld* world,
    NebulaNativeWorldStats* out_stats);

#ifdef __cplusplus
}
#endif
