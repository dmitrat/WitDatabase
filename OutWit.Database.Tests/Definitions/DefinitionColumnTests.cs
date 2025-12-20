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
    public class DefinitionColumnTests
    {
        [Test]
        public void ConstructorTest()
        {
            var definition = new DefinitionColumn
            {
                Name = "Id",
                Type = WitDataType.Int32,
                Ordinal = 0
            };

            Assert.That(definition.Name, Is.EqualTo("Id"));
            Assert.That(definition.Type, Is.EqualTo(WitDataType.Int32));
            Assert.That(definition.Nullable, Is.True);
            Assert.That(definition.IsPrimaryKey, Is.False);
            Assert.That(definition.IsAutoIncrement, Is.False);
            Assert.That(definition.IsUnique, Is.False);
            Assert.That(definition.DefaultValue, Is.Null);
            Assert.That(definition.Ordinal, Is.EqualTo(0));
            Assert.That(definition.CheckExpression, Is.Null);
            Assert.That(definition.ForeignKey, Is.Null);

            definition = new DefinitionColumn
            {
                Name = "Id",
                Type = WitDataType.Int32,
                Nullable = false,
                IsPrimaryKey = true,
                IsAutoIncrement = true,
                IsUnique = true,
                DefaultValue = "0",
                Ordinal = 0,
                CheckExpression = "Id > 0",
                ForeignKey = new DefinitionForeignKey
                {
                    Columns = new[] { "Id" },
                    ForeignTable = "OtherTable"
                }
            };

            Assert.That(definition.Name, Is.EqualTo("Id"));
            Assert.That(definition.Type, Is.EqualTo(WitDataType.Int32));
            Assert.That(definition.Nullable, Is.False);
            Assert.That(definition.IsPrimaryKey, Is.True);
            Assert.That(definition.IsAutoIncrement, Is.True);
            Assert.That(definition.IsUnique, Is.True);
            Assert.That(definition.DefaultValue, Is.EqualTo("0"));
            Assert.That(definition.Ordinal, Is.EqualTo(0));
            Assert.That(definition.CheckExpression, Is.EqualTo("Id > 0"));
            Assert.That(definition.ForeignKey, Is.Not.Null);
        }

        [Test]
        public void IsTest()
        {
            var definition = new DefinitionColumn
            {
                Name = "Id",
                Type = WitDataType.Int32,
                Nullable = false,
                IsPrimaryKey = true,
                IsAutoIncrement = true,
                IsUnique = true,
                DefaultValue = "0",
                Ordinal = 0,
                CheckExpression = "Id > 0",
                ForeignKey = new DefinitionForeignKey
                {
                    Columns = new[] { "Id" },
                    ForeignTable = "OtherTable"
                }
            };

            Assert.That(definition, Was.EqualTo(definition.Clone()));
            Assert.That(definition, Was.Not.EqualTo(definition.With(col => col.Name, "Name")));
            Assert.That(definition, Was.Not.EqualTo(definition.With(col => col.Type, WitDataType.StringVariable)));
            Assert.That(definition, Was.Not.EqualTo(definition.With(col => col.Nullable, true)));
            Assert.That(definition, Was.Not.EqualTo(definition.With(col => col.IsPrimaryKey, false)));
            Assert.That(definition, Was.Not.EqualTo(definition.With(col => col.IsAutoIncrement, false)));
            Assert.That(definition, Was.Not.EqualTo(definition.With(col => col.IsUnique, false)));
            Assert.That(definition, Was.Not.EqualTo(definition.With(col => col.DefaultValue, "1")));
            Assert.That(definition, Was.Not.EqualTo(definition.With(col => col.Ordinal, 1)));
            Assert.That(definition, Was.Not.EqualTo(definition.With(col => col.CheckExpression, "Id > 10")));
            Assert.That(definition, Was.Not.EqualTo(definition.With(col => col.ForeignKey, null)));
        }

        [Test]
        public void CloneTest()
        {
            var definition = new DefinitionColumn
            {
                Name = "Id",
                Type = WitDataType.Int32,
                Nullable = false,
                IsPrimaryKey = true,
                IsAutoIncrement = true,
                IsUnique = true,
                DefaultValue = "0",
                Ordinal = 0,
                CheckExpression = "Id > 0",
                ForeignKey = new DefinitionForeignKey
                {
                    Columns = new[] { "Id" },
                    ForeignTable = "OtherTable"
                }
            };

            var clone = definition.Clone();

            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(definition));

            Assert.That(clone.Name, Is.EqualTo("Id"));
            Assert.That(clone.Type, Is.EqualTo(WitDataType.Int32));
            Assert.That(clone.Nullable, Is.False);
            Assert.That(clone.IsPrimaryKey, Is.True);
            Assert.That(clone.IsAutoIncrement, Is.True);
            Assert.That(clone.IsUnique, Is.True);
            Assert.That(clone.DefaultValue, Is.EqualTo("0"));
            Assert.That(clone.Ordinal, Is.EqualTo(0));
            Assert.That(clone.CheckExpression, Is.EqualTo("Id > 0"));
            Assert.That(clone.ForeignKey, Is.Not.Null);
            Assert.That(clone.ForeignKey.ForeignTable, Is.EqualTo("OtherTable"));
        }

        [Test]
        public void JsonCloneTest()
        {
            var definition = new DefinitionColumn
            {
                Name = "Id",
                Type = WitDataType.Int32,
                Nullable = false,
                IsPrimaryKey = true,
                IsAutoIncrement = true,
                IsUnique = true,
                DefaultValue = "0",
                Ordinal = 0,
                CheckExpression = "Id > 0",
                ForeignKey = new DefinitionForeignKey
                {
                    Columns = new[] { "Id" },
                    ForeignTable = "OtherTable"
                }
            };

            var clone = definition.Clone();

            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(definition));

            Assert.That(clone.Name, Is.EqualTo("Id"));
            Assert.That(clone.Type, Is.EqualTo(WitDataType.Int32));
            Assert.That(clone.Nullable, Is.False);
            Assert.That(clone.IsPrimaryKey, Is.True);
            Assert.That(clone.IsAutoIncrement, Is.True);
            Assert.That(clone.IsUnique, Is.True);
            Assert.That(clone.DefaultValue, Is.EqualTo("0"));
            Assert.That(clone.Ordinal, Is.EqualTo(0));
            Assert.That(clone.CheckExpression, Is.EqualTo("Id > 0"));
            Assert.That(clone.ForeignKey, Is.Not.Null);
            Assert.That(clone.ForeignKey.ForeignTable, Is.EqualTo("OtherTable"));
        }
    }
}
