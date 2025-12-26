using NUnit.Framework.Legacy;
using OutWit.Common.NUnit;
using OutWit.Database.Definitions;
using OutWit.Common.Collections;
using OutWit.Common.MemoryPack;
using OutWit.Common.Utils;
using OutWit.Database.Types;

namespace OutWit.Database.Tests.Definitions
{
    [TestFixture]
    public class DefinitionColumnTests
    {
        #region Constructor Tests

        [Test]
        public void ConstructorWithDefaultsTest()
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
            Assert.That(definition.MaxLength, Is.Null);
            Assert.That(definition.Precision, Is.Null);
            Assert.That(definition.Scale, Is.Null);
            Assert.That(definition.ComputedExpression, Is.Null);
            Assert.That(definition.IsStored, Is.False);
            Assert.That(definition.Collation, Is.Null);
            Assert.That(definition.ConstraintName, Is.Null);
            Assert.That(definition.IsComputed, Is.False);
        }

        [Test]
        public void ConstructorWithAllPropertiesTest()
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
                },
                MaxLength = 100,
                Precision = 18,
                Scale = 4,
                ComputedExpression = "Price * Quantity",
                IsStored = true,
                Collation = "NOCASE",
                ConstraintName = "PK_MyTable_Id"
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
            Assert.That(definition.MaxLength, Is.EqualTo(100));
            Assert.That(definition.Precision, Is.EqualTo(18));
            Assert.That(definition.Scale, Is.EqualTo(4));
            Assert.That(definition.ComputedExpression, Is.EqualTo("Price * Quantity"));
            Assert.That(definition.IsStored, Is.True);
            Assert.That(definition.Collation, Is.EqualTo("NOCASE"));
            Assert.That(definition.ConstraintName, Is.EqualTo("PK_MyTable_Id"));
            Assert.That(definition.IsComputed, Is.True);
        }

        [Test]
        public void ConstructorVarcharWithMaxLengthTest()
        {
            var definition = new DefinitionColumn
            {
                Name = "Email",
                Type = WitDataType.StringVariable,
                Ordinal = 0,
                MaxLength = 255,
                Collation = "NOCASE"
            };

            Assert.That(definition.Name, Is.EqualTo("Email"));
            Assert.That(definition.Type, Is.EqualTo(WitDataType.StringVariable));
            Assert.That(definition.MaxLength, Is.EqualTo(255));
            Assert.That(definition.Collation, Is.EqualTo("NOCASE"));
        }

        [Test]
        public void ConstructorDecimalWithPrecisionScaleTest()
        {
            var definition = new DefinitionColumn
            {
                Name = "Price",
                Type = WitDataType.Decimal,
                Ordinal = 0,
                Precision = 18,
                Scale = 4
            };

            Assert.That(definition.Name, Is.EqualTo("Price"));
            Assert.That(definition.Type, Is.EqualTo(WitDataType.Decimal));
            Assert.That(definition.Precision, Is.EqualTo(18));
            Assert.That(definition.Scale, Is.EqualTo(4));
        }

        [Test]
        public void ConstructorComputedColumnTest()
        {
            var definition = new DefinitionColumn
            {
                Name = "TotalPrice",
                Type = WitDataType.Decimal,
                Ordinal = 0,
                ComputedExpression = "Price * Quantity",
                IsStored = true
            };

            Assert.That(definition.Name, Is.EqualTo("TotalPrice"));
            Assert.That(definition.ComputedExpression, Is.EqualTo("Price * Quantity"));
            Assert.That(definition.IsStored, Is.True);
            Assert.That(definition.IsComputed, Is.True);
        }

        [Test]
        public void ConstructorRowVersionTest()
        {
            var definition = new DefinitionColumn
            {
                Name = "Version",
                Type = WitDataType.RowVersion,
                Ordinal = 0,
                Nullable = false
            };

            Assert.That(definition.Name, Is.EqualTo("Version"));
            Assert.That(definition.Type, Is.EqualTo(WitDataType.RowVersion));
            Assert.That(definition.Nullable, Is.False);
        }

        [Test]
        public void ConstructorJsonTest()
        {
            var definition = new DefinitionColumn
            {
                Name = "Metadata",
                Type = WitDataType.Json,
                Ordinal = 0
            };

            Assert.That(definition.Name, Is.EqualTo("Metadata"));
            Assert.That(definition.Type, Is.EqualTo(WitDataType.Json));
        }

        #endregion

        #region Is Tests

        [Test]
        public void IsEqualToCloneTest()
        {
            var definition = CreateFullDefinition();
            Assert.That(definition, Was.EqualTo(definition.Clone()));
        }

        [Test]
        public void IsNotEqualWhenNameDifferentTest()
        {
            var definition = CreateFullDefinition();
            Assert.That(definition, Was.Not.EqualTo(definition.With(col => col.Name, "Name")));
        }

        [Test]
        public void IsNotEqualWhenTypeDifferentTest()
        {
            var definition = CreateFullDefinition();
            Assert.That(definition, Was.Not.EqualTo(definition.With(col => col.Type, WitDataType.StringVariable)));
        }

        [Test]
        public void IsNotEqualWhenNullableDifferentTest()
        {
            var definition = CreateFullDefinition();
            Assert.That(definition, Was.Not.EqualTo(definition.With(col => col.Nullable, true)));
        }

        [Test]
        public void IsNotEqualWhenIsPrimaryKeyDifferentTest()
        {
            var definition = CreateFullDefinition();
            Assert.That(definition, Was.Not.EqualTo(definition.With(col => col.IsPrimaryKey, false)));
        }

        [Test]
        public void IsNotEqualWhenIsAutoIncrementDifferentTest()
        {
            var definition = CreateFullDefinition();
            Assert.That(definition, Was.Not.EqualTo(definition.With(col => col.IsAutoIncrement, false)));
        }

        [Test]
        public void IsNotEqualWhenIsUniqueDifferentTest()
        {
            var definition = CreateFullDefinition();
            Assert.That(definition, Was.Not.EqualTo(definition.With(col => col.IsUnique, false)));
        }

        [Test]
        public void IsNotEqualWhenDefaultValueDifferentTest()
        {
            var definition = CreateFullDefinition();
            Assert.That(definition, Was.Not.EqualTo(definition.With(col => col.DefaultValue, "1")));
        }

        [Test]
        public void IsNotEqualWhenOrdinalDifferentTest()
        {
            var definition = CreateFullDefinition();
            Assert.That(definition, Was.Not.EqualTo(definition.With(col => col.Ordinal, 1)));
        }

        [Test]
        public void IsNotEqualWhenCheckExpressionDifferentTest()
        {
            var definition = CreateFullDefinition();
            Assert.That(definition, Was.Not.EqualTo(definition.With(col => col.CheckExpression, "Id > 10")));
        }

        [Test]
        public void IsNotEqualWhenForeignKeyNullTest()
        {
            var definition = CreateFullDefinition();
            Assert.That(definition, Was.Not.EqualTo(definition.With(col => col.ForeignKey, null)));
        }

        [Test]
        public void IsNotEqualWhenMaxLengthDifferentTest()
        {
            var definition = CreateFullDefinition();
            Assert.That(definition, Was.Not.EqualTo(definition.With(col => col.MaxLength, 200)));
        }

        [Test]
        public void IsNotEqualWhenPrecisionDifferentTest()
        {
            var definition = CreateFullDefinition();
            Assert.That(definition, Was.Not.EqualTo(definition.With(col => col.Precision, 10)));
        }

        [Test]
        public void IsNotEqualWhenScaleDifferentTest()
        {
            var definition = CreateFullDefinition();
            Assert.That(definition, Was.Not.EqualTo(definition.With(col => col.Scale, 2)));
        }

        [Test]
        public void IsNotEqualWhenComputedExpressionDifferentTest()
        {
            var definition = CreateFullDefinition();
            Assert.That(definition, Was.Not.EqualTo(definition.With(col => col.ComputedExpression, "Other * Value")));
        }

        [Test]
        public void IsNotEqualWhenIsStoredDifferentTest()
        {
            var definition = CreateFullDefinition();
            Assert.That(definition, Was.Not.EqualTo(definition.With(col => col.IsStored, false)));
        }

        [Test]
        public void IsNotEqualWhenCollationDifferentTest()
        {
            var definition = CreateFullDefinition();
            Assert.That(definition, Was.Not.EqualTo(definition.With(col => col.Collation, "BINARY")));
        }

        [Test]
        public void IsNotEqualWhenConstraintNameDifferentTest()
        {
            var definition = CreateFullDefinition();
            Assert.That(definition, Was.Not.EqualTo(definition.With(col => col.ConstraintName, "Other_PK")));
        }

        #endregion

        #region Clone Tests

        [Test]
        public void CloneTest()
        {
            var definition = CreateFullDefinition();
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
            Assert.That(clone.MaxLength, Is.EqualTo(100));
            Assert.That(clone.Precision, Is.EqualTo(18));
            Assert.That(clone.Scale, Is.EqualTo(4));
            Assert.That(clone.ComputedExpression, Is.EqualTo("Price * Quantity"));
            Assert.That(clone.IsStored, Is.True);
            Assert.That(clone.Collation, Is.EqualTo("NOCASE"));
            Assert.That(clone.ConstraintName, Is.EqualTo("PK_MyTable_Id"));
        }

        [Test]
        public void CloneForeignKeyIsClonedTest()
        {
            var definition = CreateFullDefinition();
            var clone = definition.Clone();

            Assert.That(clone.ForeignKey, Is.Not.SameAs(definition.ForeignKey));
        }

        #endregion

        #region MemoryPack Tests

        [Test]
        public void MemoryPackCloneTest()
        {
            var definition = CreateFullDefinition();
            var clone = definition.MemoryPackClone();

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
            Assert.That(clone.MaxLength, Is.EqualTo(100));
            Assert.That(clone.Precision, Is.EqualTo(18));
            Assert.That(clone.Scale, Is.EqualTo(4));
            Assert.That(clone.ComputedExpression, Is.EqualTo("Price * Quantity"));
            Assert.That(clone.IsStored, Is.True);
            Assert.That(clone.Collation, Is.EqualTo("NOCASE"));
            Assert.That(clone.ConstraintName, Is.EqualTo("PK_MyTable_Id"));
        }

        [Test]
        public void MemoryPackCloneWithNullsTest()
        {
            var definition = new DefinitionColumn
            {
                Name = "Simple",
                Type = WitDataType.Int32,
                Ordinal = 0
            };

            var clone = definition.MemoryPackClone();

            Assert.That(clone, Is.Not.Null);
            Assert.That(clone.MaxLength, Is.Null);
            Assert.That(clone.Precision, Is.Null);
            Assert.That(clone.Scale, Is.Null);
            Assert.That(clone.ComputedExpression, Is.Null);
            Assert.That(clone.Collation, Is.Null);
            Assert.That(clone.ConstraintName, Is.Null);
        }

        #endregion

        #region ToString Tests

        [Test]
        public void ToStringSimpleColumnTest()
        {
            var definition = new DefinitionColumn
            {
                Name = "Id",
                Type = WitDataType.Int32,
                Ordinal = 0
            };

            Assert.That(definition.ToString(), Is.EqualTo("Id Int32"));
        }

        [Test]
        public void ToStringNotNullColumnTest()
        {
            var definition = new DefinitionColumn
            {
                Name = "Id",
                Type = WitDataType.Int32,
                Nullable = false,
                Ordinal = 0
            };

            Assert.That(definition.ToString(), Is.EqualTo("Id Int32 NOT NULL"));
        }

        [Test]
        public void ToStringPrimaryKeyColumnTest()
        {
            var definition = new DefinitionColumn
            {
                Name = "Id",
                Type = WitDataType.Int32,
                Nullable = false,
                IsPrimaryKey = true,
                Ordinal = 0
            };

            Assert.That(definition.ToString(), Is.EqualTo("Id Int32 NOT NULL PRIMARY KEY"));
        }

        [Test]
        public void ToStringWithMaxLengthTest()
        {
            var definition = new DefinitionColumn
            {
                Name = "Email",
                Type = WitDataType.StringVariable,
                MaxLength = 255,
                Ordinal = 0
            };

            Assert.That(definition.ToString(), Is.EqualTo("Email StringVariable(255)"));
        }

        [Test]
        public void ToStringWithPrecisionAndScaleTest()
        {
            var definition = new DefinitionColumn
            {
                Name = "Price",
                Type = WitDataType.Decimal,
                Precision = 18,
                Scale = 4,
                Ordinal = 0
            };

            Assert.That(definition.ToString(), Is.EqualTo("Price Decimal(18,4)"));
        }

        [Test]
        public void ToStringWithPrecisionOnlyTest()
        {
            var definition = new DefinitionColumn
            {
                Name = "Amount",
                Type = WitDataType.Decimal,
                Precision = 10,
                Ordinal = 0
            };

            Assert.That(definition.ToString(), Is.EqualTo("Amount Decimal(10)"));
        }

        #endregion

        #region Helpers

        private static DefinitionColumn CreateFullDefinition()
        {
            return new DefinitionColumn
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
                },
                MaxLength = 100,
                Precision = 18,
                Scale = 4,
                ComputedExpression = "Price * Quantity",
                IsStored = true,
                Collation = "NOCASE",
                ConstraintName = "PK_MyTable_Id"
            };
        }

        #endregion
    }
}
