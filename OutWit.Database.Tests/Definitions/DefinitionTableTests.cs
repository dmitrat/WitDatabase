using NUnit.Framework.Legacy;
using OutWit.Common.NUnit;
using OutWit.Database.Definitions;
using System;
using System.Collections.Generic;
using System.Text;
using OutWit.Common.Collections;
using OutWit.Common.Utils;
using OutWit.Database.Types;

namespace OutWit.Database.Tests.Definitions
{
    [TestFixture]
    public class DefinitionTableTests
    {
        [Test]
        public void ConstructorTest()
        {
            var definition = new DefinitionTable
            {
                Name = "Users",
                Columns = new[]
                {
                    new DefinitionColumn
                    {
                        Name = "Id",
                        Type = WitDataType.Int32,
                        IsPrimaryKey = true,
                        Ordinal = 0
                    }
                }
            };

            Assert.That(definition.Name, Is.EqualTo("Users"));
            Assert.That(definition.Columns.Count, Is.EqualTo(1));
            Assert.That(definition.Columns[0].Name, Is.EqualTo("Id"));
            Assert.That(definition.PrimaryKey, Is.Null);
            Assert.That(definition.RowIdColumn, Is.EqualTo("_rowid"));
            Assert.That(definition.AutoIncrementRowId, Is.True);
            Assert.That(definition.CheckExpressions, Is.Null);
            Assert.That(definition.ForeignKeys, Is.Null);
            Assert.That(definition.UniqueConstraints, Is.Null);

            definition = new DefinitionTable
            {
                Name = "Users",
                Columns = new[]
                {
                    new DefinitionColumn
                    {
                        Name = "Id",
                        Type = WitDataType.Int32,
                        IsPrimaryKey = true,
                        Ordinal = 0
                    },
                    new DefinitionColumn
                    {
                        Name = "Name",
                        Type = WitDataType.StringVariable,
                        Ordinal = 1
                    }
                },
                PrimaryKey = new[] { "Id" },
                RowIdColumn = "Id",
                AutoIncrementRowId = false,
                CheckExpressions = new[] { "Id > 0" },
                ForeignKeys = new[]
                {
                    new DefinitionForeignKey
                    {
                        Columns = new[] { "Id" },
                        ForeignTable = "OtherTable",
                        OnDelete = ReferenceAction.Cascade
                    }
                },
                UniqueConstraints = new[] { new[] { "Name" }.AsReadOnly() }
            };

            Assert.That(definition.Name, Is.EqualTo("Users"));
            Assert.That(definition.Columns.Count, Is.EqualTo(2));
            Assert.That(definition.PrimaryKey.Is("Id"), Is.True);
            Assert.That(definition.RowIdColumn, Is.EqualTo("Id"));
            Assert.That(definition.AutoIncrementRowId, Is.False);
            Assert.That(definition.CheckExpressions.Is("Id > 0"), Is.True);
            Assert.That(definition.ForeignKeys.Count, Is.EqualTo(1));
            Assert.That(definition.UniqueConstraints.Count, Is.EqualTo(1));
        }

        [Test]
        public void IsTest()
        {
            var definition = new DefinitionTable
            {
                Name = "Users",
                Columns = new[]
                {
                    new DefinitionColumn
                    {
                        Name = "Id",
                        Type = WitDataType.Int32,
                        IsPrimaryKey = true,
                        Ordinal = 0
                    },
                    new DefinitionColumn
                    {
                        Name = "Name",
                        Type = WitDataType.StringVariable,
                        Ordinal = 1
                    }
                },
                PrimaryKey = new[] { "Id" },
                RowIdColumn = "Id",
                AutoIncrementRowId = false,
                CheckExpressions = new[] { "Id > 0" },
                ForeignKeys = new[]
                {
                    new DefinitionForeignKey
                    {
                        Columns = new[] { "Id" },
                        ForeignTable = "OtherTable",
                        OnDelete = ReferenceAction.Cascade
                    }
                },
                UniqueConstraints = new[] { new[] { "Name" }.AsReadOnly() }
            };

            Assert.That(definition, Was.EqualTo(definition.Clone()));
            Assert.That(definition, Was.Not.EqualTo(definition.With(table => table.Name, "Orders")));
            Assert.That(definition, Was.Not.EqualTo(definition.With(table => table.Columns, new[]
            {
                new DefinitionColumn
                {
                    Name = "Id",
                    Type = WitDataType.Int32,
                    IsPrimaryKey = true,
                    Ordinal = 0
                }
            })));
            Assert.That(definition, Was.Not.EqualTo(definition.With(table => table.PrimaryKey, new[] { "Name" })));
            Assert.That(definition, Was.Not.EqualTo(definition.With(table => table.RowIdColumn, "_rowid")));
            Assert.That(definition, Was.Not.EqualTo(definition.With(table => table.AutoIncrementRowId, true)));
            Assert.That(definition, Was.Not.EqualTo(definition.With(table => table.CheckExpressions, new[] { "Id < 0" })));
            Assert.That(definition, Was.Not.EqualTo(definition.With(table => table.ForeignKeys, new[]
            {
                new DefinitionForeignKey
                {
                    Columns = new[] { "Id" },
                    ForeignTable = "DifferentTable",
                    OnDelete = ReferenceAction.Cascade
                }
            })));
            Assert.That(definition, Was.Not.EqualTo(definition.With(table => table.UniqueConstraints, new[] { new[] { "Id" }.AsReadOnly() })));
        }

        [Test]
        public void CloneTest()
        {
            var definition = new DefinitionTable
            {
                Name = "Users",
                Columns = new[]
                {
                    new DefinitionColumn
                    {
                        Name = "Id",
                        Type = WitDataType.Int32,
                        IsPrimaryKey = true,
                        Ordinal = 0
                    },
                    new DefinitionColumn
                    {
                        Name = "Name",
                        Type = WitDataType.StringVariable,
                        Ordinal = 1
                    }
                },
                PrimaryKey = new[] { "Id" },
                RowIdColumn = "Id",
                AutoIncrementRowId = false,
                CheckExpressions = new[] { "Id > 0" },
                ForeignKeys = new[]
                {
                    new DefinitionForeignKey
                    {
                        Columns = new[] { "Id" },
                        ForeignTable = "OtherTable",
                        OnDelete = ReferenceAction.Cascade
                    }
                },
                UniqueConstraints = new[] { new[] { "Name" }.AsReadOnly() }
            };

            var clone = definition.Clone();

            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(definition));

            Assert.That(clone.Name, Is.EqualTo("Users"));
            Assert.That(clone.Columns.Count, Is.EqualTo(2));
            Assert.That(clone.Columns[0].Name, Is.EqualTo("Id"));
            Assert.That(clone.Columns[1].Name, Is.EqualTo("Name"));
            Assert.That(clone.PrimaryKey.Is("Id"), Is.True);
            Assert.That(clone.RowIdColumn, Is.EqualTo("Id"));
            Assert.That(clone.AutoIncrementRowId, Is.False);
            Assert.That(clone.CheckExpressions.Is("Id > 0"), Is.True);
            Assert.That(clone.ForeignKeys.Count, Is.EqualTo(1));
            Assert.That(clone.UniqueConstraints.Count, Is.EqualTo(1));
        }

        [Test]
        public void JsonCloneTest()
        {
            var definition = new DefinitionTable
            {
                Name = "Users",
                Columns = new[]
                {
                    new DefinitionColumn
                    {
                        Name = "Id",
                        Type = WitDataType.Int32,
                        IsPrimaryKey = true,
                        Ordinal = 0
                    },
                    new DefinitionColumn
                    {
                        Name = "Name",
                        Type = WitDataType.StringVariable,
                        Ordinal = 1
                    }
                },
                PrimaryKey = new[] { "Id" },
                RowIdColumn = "Id",
                AutoIncrementRowId = false,
                CheckExpressions = new[] { "Id > 0" },
                ForeignKeys = new[]
                {
                    new DefinitionForeignKey
                    {
                        Columns = new[] { "Id" },
                        ForeignTable = "OtherTable",
                        OnDelete = ReferenceAction.Cascade
                    }
                },
                UniqueConstraints = new[] { new[] { "Name" }.AsReadOnly() }
            };

            var clone = definition.Clone();

            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(definition));

            Assert.That(clone.Name, Is.EqualTo("Users"));
            Assert.That(clone.Columns.Count, Is.EqualTo(2));
            Assert.That(clone.Columns[0].Name, Is.EqualTo("Id"));
            Assert.That(clone.Columns[1].Name, Is.EqualTo("Name"));
            Assert.That(clone.PrimaryKey.Is("Id"), Is.True);
            Assert.That(clone.RowIdColumn, Is.EqualTo("Id"));
            Assert.That(clone.AutoIncrementRowId, Is.False);
            Assert.That(clone.CheckExpressions.Is("Id > 0"), Is.True);
            Assert.That(clone.ForeignKeys.Count, Is.EqualTo(1));
            Assert.That(clone.UniqueConstraints.Count, Is.EqualTo(1));
        }

        [Test]
        public void GetColumnTest()
        {
            var definition = new DefinitionTable
            {
                Name = "Users",
                Columns = new[]
                {
                    new DefinitionColumn
                    {
                        Name = "Id",
                        Type = WitDataType.Int32,
                        Ordinal = 0
                    },
                    new DefinitionColumn
                    {
                        Name = "Name",
                        Type = WitDataType.StringVariable,
                        Ordinal = 1
                    }
                }
            };

            var column = definition.GetColumn("Id");
            Assert.That(column, Is.Not.Null);
            Assert.That(column.Name, Is.EqualTo("Id"));

            column = definition.GetColumn("name");
            Assert.That(column, Is.Not.Null);
            Assert.That(column.Name, Is.EqualTo("Name"));

            column = definition.GetColumn("NonExistent");
            Assert.That(column, Is.Null);
        }

        [Test]
        public void GetOrdinalTest()
        {
            var definition = new DefinitionTable
            {
                Name = "Users",
                Columns = new[]
                {
                    new DefinitionColumn
                    {
                        Name = "Id",
                        Type = WitDataType.Int32,
                        Ordinal = 0
                    },
                    new DefinitionColumn
                    {
                        Name = "Name",
                        Type = WitDataType.StringVariable,
                        Ordinal = 1
                    }
                }
            };

            Assert.That(definition.GetOrdinal("Id"), Is.EqualTo(0));
            Assert.That(definition.GetOrdinal("name"), Is.EqualTo(1));
            Assert.That(definition.GetOrdinal("NonExistent"), Is.EqualTo(-1));
        }
    }
}
