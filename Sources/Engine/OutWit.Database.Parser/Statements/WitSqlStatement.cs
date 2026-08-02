using MemoryPack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OutWit.Database.Parser.Nodes;

namespace OutWit.Database.Parser.Statements
{
    [MemoryPackable]
    // Persisted format. Tags are append-only: never renumber one and
    // never reuse a retired one, or old files read back as the wrong type.
    [MemoryPackUnion(0, typeof(WitSqlStatementAlterSequence))]
    [MemoryPackUnion(1, typeof(WitSqlStatementAlterTable))]
    [MemoryPackUnion(2, typeof(WitSqlStatementBeginTransaction))]
    [MemoryPackUnion(3, typeof(WitSqlStatementCommit))]
    [MemoryPackUnion(4, typeof(WitSqlStatementCreateIndex))]
    [MemoryPackUnion(5, typeof(WitSqlStatementCreateSequence))]
    [MemoryPackUnion(6, typeof(WitSqlStatementCreateTable))]
    [MemoryPackUnion(7, typeof(WitSqlStatementCreateTrigger))]
    [MemoryPackUnion(8, typeof(WitSqlStatementCreateView))]
    [MemoryPackUnion(9, typeof(WitSqlStatementDelete))]
    [MemoryPackUnion(10, typeof(WitSqlStatementDropIndex))]
    [MemoryPackUnion(11, typeof(WitSqlStatementDropSequence))]
    [MemoryPackUnion(12, typeof(WitSqlStatementDropTable))]
    [MemoryPackUnion(13, typeof(WitSqlStatementDropTrigger))]
    [MemoryPackUnion(14, typeof(WitSqlStatementDropView))]
    [MemoryPackUnion(15, typeof(WitSqlStatementExplain))]
    [MemoryPackUnion(16, typeof(WitSqlStatementInsert))]
    [MemoryPackUnion(17, typeof(WitSqlStatementMerge))]
    [MemoryPackUnion(18, typeof(WitSqlStatementReleaseSavepoint))]
    [MemoryPackUnion(19, typeof(WitSqlStatementRollback))]
    [MemoryPackUnion(20, typeof(WitSqlStatementSavepoint))]
    [MemoryPackUnion(21, typeof(WitSqlStatementSelect))]
    [MemoryPackUnion(22, typeof(WitSqlStatementSetTransaction))]
    [MemoryPackUnion(23, typeof(WitSqlStatementSignal))]
    [MemoryPackUnion(24, typeof(WitSqlStatementTruncate))]
    [MemoryPackUnion(25, typeof(WitSqlStatementUpdate))]
    // Routines, phase 9d. Appended, never inserted: the numbers are what a stored procedure body
    // deserialises by, and renumbering one would make an old file read back as a different type
    // without a word.
    [MemoryPackUnion(26, typeof(WitSqlStatementCreateFunction))]
    [MemoryPackUnion(27, typeof(WitSqlStatementDropFunction))]
    [MemoryPackUnion(28, typeof(WitSqlStatementCreateProcedure))]
    [MemoryPackUnion(29, typeof(WitSqlStatementDropProcedure))]
    [MemoryPackUnion(30, typeof(WitSqlStatementCall))]
    public abstract partial class WitSqlStatement : WitSqlNode
    {
    }
}
