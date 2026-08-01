using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MemoryPack;
using OutWit.Database.Parser.Nodes;

namespace OutWit.Database.Parser.Expressions
{
    [MemoryPackable]
    // Persisted format. Tags are append-only: never renumber one and
    // never reuse a retired one, or old files read back as the wrong type.
    [MemoryPackUnion(0, typeof(WitSqlExpressionBetween))]
    [MemoryPackUnion(1, typeof(WitSqlExpressionBinary))]
    [MemoryPackUnion(2, typeof(WitSqlExpressionCase))]
    [MemoryPackUnion(3, typeof(WitSqlExpressionCast))]
    [MemoryPackUnion(4, typeof(WitSqlExpressionCollate))]
    [MemoryPackUnion(5, typeof(WitSqlExpressionColumnRef))]
    [MemoryPackUnion(6, typeof(WitSqlExpressionExists))]
    [MemoryPackUnion(7, typeof(WitSqlExpressionFunctionCall))]
    [MemoryPackUnion(8, typeof(WitSqlExpressionGlob))]
    [MemoryPackUnion(9, typeof(WitSqlExpressionIif))]
    [MemoryPackUnion(10, typeof(WitSqlExpressionIn))]
    [MemoryPackUnion(11, typeof(WitSqlExpressionIsNull))]
    [MemoryPackUnion(12, typeof(WitSqlExpressionLike))]
    [MemoryPackUnion(13, typeof(WitSqlExpressionLiteral))]
    [MemoryPackUnion(14, typeof(WitSqlExpressionOrderByColumnIndex))]
    [MemoryPackUnion(15, typeof(WitSqlExpressionParameter))]
    [MemoryPackUnion(16, typeof(WitSqlExpressionQuantified))]
    [MemoryPackUnion(17, typeof(WitSqlExpressionSubquery))]
    [MemoryPackUnion(18, typeof(WitSqlExpressionUnary))]
    public abstract partial class WitSqlExpression : WitSqlNode
    {
    }
}
