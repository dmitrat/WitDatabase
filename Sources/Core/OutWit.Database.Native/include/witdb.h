#ifndef WITDB_H
#define WITDB_H

#include <stdint.h>

#ifdef _WIN32
#define WITDB_API __cdecl
#else
#define WITDB_API
#endif

#define WITDB_ABI_VERSION 1u

typedef enum WitDbStatus {
    WITDB_OK = 0,
    WITDB_INVALID_ARGUMENT = 1,
    WITDB_NOT_FOUND = 2,
    WITDB_PASSWORD_REQUIRED = 3,
    WITDB_WRONG_PASSWORD = 4,
    WITDB_CONFIG_MISMATCH = 5,
    WITDB_UNKNOWN_PROVIDER = 6,
    WITDB_TXN_NOT_SUPPORTED = 7,
    WITDB_TXN_ACTIVE = 8,
    WITDB_STORE_ERROR = 9,
    WITDB_INVALID_HANDLE = 10
} WitDbStatus;

#if defined(__cplusplus)
extern "C" {
#endif

uint32_t WITDB_API witdb_abi_version(void);
const char* WITDB_API witdb_last_error_message(void);

WitDbStatus WITDB_API witdb_open(
    const char* path,
    const char* password,
    uint8_t create_if_missing,
    uintptr_t* out_db);

WitDbStatus WITDB_API witdb_close(uintptr_t db);

WitDbStatus WITDB_API witdb_get(
    uintptr_t db,
    const uint8_t* key,
    uint32_t key_len,
    uint8_t** out_value,
    uint32_t* out_value_len);

WitDbStatus WITDB_API witdb_put(
    uintptr_t db,
    const uint8_t* key,
    uint32_t key_len,
    const uint8_t* value,
    uint32_t value_len);

WitDbStatus WITDB_API witdb_delete(
    uintptr_t db,
    const uint8_t* key,
    uint32_t key_len,
    uint8_t* out_deleted);

WitDbStatus WITDB_API witdb_txn_begin(uintptr_t db, uintptr_t* out_txn);
WitDbStatus WITDB_API witdb_txn_commit(uintptr_t txn);
WitDbStatus WITDB_API witdb_txn_rollback(uintptr_t txn);

WitDbStatus WITDB_API witdb_txn_get(
    uintptr_t txn,
    const uint8_t* key,
    uint32_t key_len,
    uint8_t** out_value,
    uint32_t* out_value_len);

WitDbStatus WITDB_API witdb_txn_put(
    uintptr_t txn,
    const uint8_t* key,
    uint32_t key_len,
    const uint8_t* value,
    uint32_t value_len);

WitDbStatus WITDB_API witdb_txn_delete(
    uintptr_t txn,
    const uint8_t* key,
    uint32_t key_len,
    uint8_t* out_deleted);

void WITDB_API witdb_buffer_free(uint8_t* ptr);

#if defined(__cplusplus)
}
#endif

#endif /* WITDB_H */
