/**
 * WitDatabase IndexedDB Helper Library
 * Provides low-level IndexedDB operations for WitDatabase storage backend.
 * 
 * @version 1.0.0
 */

(() => {
    'use strict';

    const DB_VERSION = 1;
    const PAGES_STORE = 'pages';
    const METADATA_STORE = 'metadata';
    const PAGE_COUNT_KEY = 'pageCount';

    // Connection cache to avoid reopening databases
    const connectionCache = new Map();

    /**
     * Opens or creates an IndexedDB database.
     * @param {string} databaseName - Name of the database
     * @returns {Promise<IDBDatabase>}
     */
    async function openDatabase(databaseName) {
        // Check cache first
        const cached = connectionCache.get(databaseName);
        if (cached && cached.db) {
            return cached.db;
        }

        return new Promise((resolve, reject) => {
            const request = indexedDB.open(databaseName, DB_VERSION);

            request.onerror = () => {
                reject(new Error(`Failed to open database '${databaseName}': ${request.error?.message}`));
            };

            request.onsuccess = () => {
                const db = request.result;
                connectionCache.set(databaseName, { db });
                resolve(db);
            };

            request.onupgradeneeded = (event) => {
                const db = event.target.result;

                // Create object store for pages (key = page number)
                if (!db.objectStoreNames.contains(PAGES_STORE)) {
                    db.createObjectStore(PAGES_STORE);
                }

                // Create object store for metadata
                if (!db.objectStoreNames.contains(METADATA_STORE)) {
                    db.createObjectStore(METADATA_STORE);
                }
            };
        });
    }

    /**
     * Closes an IndexedDB database connection.
     * @param {string} databaseName - Name of the database
     */
    function closeDatabase(databaseName) {
        const cached = connectionCache.get(databaseName);
        if (cached && cached.db) {
            cached.db.close();
            connectionCache.delete(databaseName);
        }
    }

    /**
     * Reads a page from the database.
     * @param {string} databaseName - Name of the database
     * @param {number} pageNumber - Page number to read
     * @returns {Promise<Uint8Array|null>} - Page data or null if not found
     */
    async function readPage(databaseName, pageNumber) {
        const db = await openDatabase(databaseName);

        return new Promise((resolve, reject) => {
            const transaction = db.transaction(PAGES_STORE, 'readonly');
            const store = transaction.objectStore(PAGES_STORE);
            const request = store.get(pageNumber);

            request.onerror = () => {
                reject(new Error(`Failed to read page ${pageNumber}: ${request.error?.message}`));
            };

            request.onsuccess = () => {
                const result = request.result;
                if (result instanceof Uint8Array) {
                    resolve(result);
                } else if (result instanceof ArrayBuffer) {
                    resolve(new Uint8Array(result));
                } else if (result) {
                    // Handle blob or other types
                    resolve(new Uint8Array(result));
                } else {
                    resolve(null);
                }
            };
        });
    }

    /**
     * Writes a page to the database.
     * @param {string} databaseName - Name of the database
     * @param {number} pageNumber - Page number to write
     * @param {Uint8Array} data - Page data
     * @returns {Promise<void>}
     */
    async function writePage(databaseName, pageNumber, data) {
        const db = await openDatabase(databaseName);

        return new Promise((resolve, reject) => {
            const transaction = db.transaction(PAGES_STORE, 'readwrite');
            const store = transaction.objectStore(PAGES_STORE);
            
            // Ensure we're storing a Uint8Array
            const pageData = data instanceof Uint8Array ? data : new Uint8Array(data);
            const request = store.put(pageData, pageNumber);

            request.onerror = () => {
                reject(new Error(`Failed to write page ${pageNumber}: ${request.error?.message}`));
            };

            transaction.oncomplete = () => {
                resolve();
            };

            transaction.onerror = () => {
                reject(new Error(`Transaction failed for page ${pageNumber}: ${transaction.error?.message}`));
            };
        });
    }

    /**
     * Writes multiple pages in a single transaction.
     * @param {string} databaseName - Name of the database
     * @param {Array<{pageNumber: number, data: Uint8Array}>} pages - Pages to write
     * @returns {Promise<void>}
     */
    async function writePages(databaseName, pages) {
        const db = await openDatabase(databaseName);

        return new Promise((resolve, reject) => {
            const transaction = db.transaction(PAGES_STORE, 'readwrite');
            const store = transaction.objectStore(PAGES_STORE);

            for (const page of pages) {
                const pageData = page.data instanceof Uint8Array ? page.data : new Uint8Array(page.data);
                store.put(pageData, page.pageNumber);
            }

            transaction.oncomplete = () => {
                resolve();
            };

            transaction.onerror = () => {
                reject(new Error(`Batch write failed: ${transaction.error?.message}`));
            };
        });
    }

    /**
     * Gets the page count from metadata.
     * @param {string} databaseName - Name of the database
     * @returns {Promise<number>}
     */
    async function getPageCount(databaseName) {
        const db = await openDatabase(databaseName);

        return new Promise((resolve, reject) => {
            const transaction = db.transaction(METADATA_STORE, 'readonly');
            const store = transaction.objectStore(METADATA_STORE);
            const request = store.get(PAGE_COUNT_KEY);

            request.onerror = () => {
                reject(new Error(`Failed to get page count: ${request.error?.message}`));
            };

            request.onsuccess = () => {
                resolve(request.result ?? 0);
            };
        });
    }

    /**
     * Sets the page count in metadata.
     * @param {string} databaseName - Name of the database
     * @param {number} count - New page count
     * @returns {Promise<void>}
     */
    async function setPageCount(databaseName, count) {
        const db = await openDatabase(databaseName);

        return new Promise((resolve, reject) => {
            const transaction = db.transaction(METADATA_STORE, 'readwrite');
            const store = transaction.objectStore(METADATA_STORE);
            const request = store.put(count, PAGE_COUNT_KEY);

            request.onerror = () => {
                reject(new Error(`Failed to set page count: ${request.error?.message}`));
            };

            transaction.oncomplete = () => {
                resolve();
            };
        });
    }

    /**
     * Gets the page size from metadata (or returns default).
     * @param {string} databaseName - Name of the database
     * @returns {Promise<number>}
     */
    async function getPageSize(databaseName) {
        const db = await openDatabase(databaseName);

        return new Promise((resolve, reject) => {
            const transaction = db.transaction(METADATA_STORE, 'readonly');
            const store = transaction.objectStore(METADATA_STORE);
            const request = store.get('pageSize');

            request.onerror = () => {
                reject(new Error(`Failed to get page size: ${request.error?.message}`));
            };

            request.onsuccess = () => {
                resolve(request.result ?? 0);
            };
        });
    }

    /**
     * Sets the page size in metadata.
     * @param {string} databaseName - Name of the database
     * @param {number} pageSize - Page size in bytes
     * @returns {Promise<void>}
     */
    async function setPageSize(databaseName, pageSize) {
        const db = await openDatabase(databaseName);

        return new Promise((resolve, reject) => {
            const transaction = db.transaction(METADATA_STORE, 'readwrite');
            const store = transaction.objectStore(METADATA_STORE);
            const request = store.put(pageSize, 'pageSize');

            request.onerror = () => {
                reject(new Error(`Failed to set page size: ${request.error?.message}`));
            };

            transaction.oncomplete = () => {
                resolve();
            };
        });
    }

    /**
     * Deletes an entire database.
     * @param {string} databaseName - Name of the database
     * @returns {Promise<void>}
     */
    async function deleteDatabase(databaseName) {
        // Close connection first
        closeDatabase(databaseName);

        return new Promise((resolve, reject) => {
            const request = indexedDB.deleteDatabase(databaseName);

            request.onerror = () => {
                reject(new Error(`Failed to delete database '${databaseName}': ${request.error?.message}`));
            };

            request.onsuccess = () => {
                resolve();
            };

            request.onblocked = () => {
                // Database is blocked, likely by another tab
                reject(new Error(`Database '${databaseName}' is blocked by another connection`));
            };
        });
    }

    /**
     * Checks if a database exists.
     * @param {string} databaseName - Name of the database
     * @returns {Promise<boolean>}
     */
    async function databaseExists(databaseName) {
        try {
            const databases = await indexedDB.databases();
            return databases.some(db => db.name === databaseName);
        } catch {
            // Firefox doesn't support indexedDB.databases()
            // Try opening and checking if upgrade was needed
            return new Promise((resolve) => {
                let existed = true;
                const request = indexedDB.open(databaseName);
                
                request.onupgradeneeded = () => {
                    existed = false;
                };
                
                request.onsuccess = () => {
                    request.result.close();
                    if (!existed) {
                        indexedDB.deleteDatabase(databaseName);
                    }
                    resolve(existed);
                };
                
                request.onerror = () => {
                    resolve(false);
                };
            });
        }
    }

    /**
     * Truncates database by removing pages beyond the specified count.
     * @param {string} databaseName - Name of the database
     * @param {number} newPageCount - New page count
     * @returns {Promise<void>}
     */
    async function truncatePages(databaseName, newPageCount) {
        const db = await openDatabase(databaseName);
        const currentCount = await getPageCount(databaseName);

        if (newPageCount >= currentCount) {
            // Just update count, no truncation needed
            await setPageCount(databaseName, newPageCount);
            return;
        }

        return new Promise((resolve, reject) => {
            const transaction = db.transaction([PAGES_STORE, METADATA_STORE], 'readwrite');
            const pagesStore = transaction.objectStore(PAGES_STORE);
            const metadataStore = transaction.objectStore(METADATA_STORE);

            // Delete pages from newPageCount to currentCount
            for (let i = newPageCount; i < currentCount; i++) {
                pagesStore.delete(i);
            }

            // Update page count
            metadataStore.put(newPageCount, PAGE_COUNT_KEY);

            transaction.oncomplete = () => {
                resolve();
            };

            transaction.onerror = () => {
                reject(new Error(`Failed to truncate pages: ${transaction.error?.message}`));
            };
        });
    }

    // Expose functions to global scope for Blazor interop
    window.witDb = {
        open: openDatabase,
        close: closeDatabase,
        readPage: readPage,
        writePage: writePage,
        writePages: writePages,
        getPageCount: getPageCount,
        setPageCount: setPageCount,
        getPageSize: getPageSize,
        setPageSize: setPageSize,
        deleteDatabase: deleteDatabase,
        databaseExists: databaseExists,
        truncatePages: truncatePages
    };

})();
