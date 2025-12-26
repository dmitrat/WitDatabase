using NUnit.Framework.Legacy;
using OutWit.Common.Collections;
using OutWit.Common.MemoryPack;
using OutWit.Common.NUnit;
using OutWit.Common.Utils;
using OutWit.Database.Definitions;

namespace OutWit.Database.Tests.Definitions
{
    [TestFixture]
    public class DefinitionIndexTests
    {
        #region Constructor Tests

        [Test]
        public void ConstructorWithDefaultsTest()
        {
            var definition = new DefinitionIndex
            {
                Name = "idx_users_name",
                TableName = "Users",
                Columns = new[] { "Name" }
            };

            Assert.That(definition.Name, Is.EqualTo("idx_users_name"));
            Assert.That(definition.TableName, Is.EqualTo("Users"));
            Assert.That(definition.Columns.Is("Name"), Is.True);
            Assert.That(definition.IsUnique, Is.False);
            Assert.That(definition.IsPrimaryKey, Is.False);
            Assert.That(definition.WhereExpression, Is.Null);
            Assert.That(definition.ExpressionColumns, Is.Null);
            Assert.That(definition.IncludeColumns, Is.Null);
            Assert.That(definition.ColumnDescending, Is.Null);
            Assert.That(definition.IsFiltered, Is.False);
            Assert.That(definition.HasExpressions, Is.False);
            Assert.That(definition.IsCovering, Is.False);
        }

        [Test]
        public void ConstructorWithAllPropertiesTest()
        {
            var definition = new DefinitionIndex
            {
                Name = "idx_users_email",
                TableName = "Users",
                Columns = new[] { "Email", "Domain" },
                IsUnique = true,
                IsPrimaryKey = false
            };

            Assert.That(definition.Name, Is.EqualTo("idx_users_email"));
            Assert.That(definition.TableName, Is.EqualTo("Users"));
            Assert.That(definition.Columns.Is("Email", "Domain"), Is.True);
            Assert.That(definition.IsUnique, Is.True);
            Assert.That(definition.IsPrimaryKey, Is.False);
        }

        [Test]
        public void ConstructorPartialIndexTest()
        {
            var definition = new DefinitionIndex
            {
                Name = "idx_orders_active",
                TableName = "Orders",
                Columns = new[] { "OrderDate" },
                WhereExpression = "Status = 'active'"
            };

            Assert.That(definition.Name, Is.EqualTo("idx_orders_active"));
            Assert.That(definition.WhereExpression, Is.EqualTo("Status = 'active'"));
            Assert.That(definition.IsFiltered, Is.True);
        }

        [Test]
        public void ConstructorExpressionIndexTest()
        {
            var definition = new DefinitionIndex
            {
                Name = "idx_users_email_lower",
                TableName = "Users",
                Columns = new[] { "Email" },
                ExpressionColumns = new[] { "LOWER(Email)" }
            };

            Assert.That(definition.Name, Is.EqualTo("idx_users_email_lower"));
            Assert.That(definition.ExpressionColumns, Is.Not.Null);
            Assert.That(definition.ExpressionColumns![0], Is.EqualTo("LOWER(Email)"));
            Assert.That(definition.HasExpressions, Is.True);
        }

        [Test]
        public void ConstructorCoveringIndexTest()
        {
            var definition = new DefinitionIndex
            {
                Name = "idx_orders_customer",
                TableName = "Orders",
                Columns = new[] { "CustomerId" },
                IncludeColumns = new[] { "OrderDate", "TotalAmount" }
            };

            Assert.That(definition.Name, Is.EqualTo("idx_orders_customer"));
            Assert.That(definition.IncludeColumns, Is.Not.Null);
            Assert.That(definition.IncludeColumns!.Count, Is.EqualTo(2));
            Assert.That(definition.IsCovering, Is.True);
        }

        [Test]
        public void ConstructorWithDescendingColumnsTest()
        {
            var definition = new DefinitionIndex
            {
                Name = "idx_orders_date",
                TableName = "Orders",
                Columns = new[] { "CustomerId", "OrderDate" },
                ColumnDescending = new[] { false, true }
            };

            Assert.That(definition.Name, Is.EqualTo("idx_orders_date"));
            Assert.That(definition.ColumnDescending, Is.Not.Null);
            Assert.That(definition.IsColumnDescending(0), Is.False);
            Assert.That(definition.IsColumnDescending(1), Is.True);
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
            Assert.That(definition, Was.Not.EqualTo(definition.With(idx => idx.Name, "idx_other")));
        }

        [Test]
        public void IsNotEqualWhenTableNameDifferentTest()
        {
            var definition = CreateFullDefinition();
            Assert.That(definition, Was.Not.EqualTo(definition.With(idx => idx.TableName, "Orders")));
        }

        [Test]
        public void IsNotEqualWhenColumnsDifferentTest()
        {
            var definition = CreateFullDefinition();
            Assert.That(definition, Was.Not.EqualTo(definition.With(idx => idx.Columns, new[] { "Email" })));
        }

        [Test]
        public void IsNotEqualWhenIsUniqueDifferentTest()
        {
            var definition = CreateFullDefinition();
            Assert.That(definition, Was.Not.EqualTo(definition.With(idx => idx.IsUnique, false)));
        }

        [Test]
        public void IsNotEqualWhenIsPrimaryKeyDifferentTest()
        {
            var definition = CreateFullDefinition();
            Assert.That(definition, Was.Not.EqualTo(definition.With(idx => idx.IsPrimaryKey, true)));
        }

        [Test]
        public void IsNotEqualWhenWhereExpressionDifferentTest()
        {
            var definition = CreateFullDefinition();
            Assert.That(definition, Was.Not.EqualTo(definition.With(idx => idx.WhereExpression, "Other = 'value'")));
        }

        [Test]
        public void IsNotEqualWhenExpressionColumnsDifferentTest()
        {
            var definition = CreateFullDefinition();
            Assert.That(definition, Was.Not.EqualTo(definition.With(idx => idx.ExpressionColumns, new string?[] { "UPPER(Email)" })));
        }

        [Test]
        public void IsNotEqualWhenIncludeColumnsDifferentTest()
        {
            var definition = CreateFullDefinition();
            Assert.That(definition, Was.Not.EqualTo(definition.With(idx => idx.IncludeColumns, new[] { "OtherCol" })));
        }

        [Test]
        public void IsNotEqualWhenColumnDescendingDifferentTest()
        {
            var definition = CreateFullDefinition();
            Assert.That(definition, Was.Not.EqualTo(definition.With(idx => idx.ColumnDescending, new[] { true, false })));
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

            Assert.That(clone.Name, Is.EqualTo("idx_users_email"));
            Assert.That(clone.TableName, Is.EqualTo("Users"));
            Assert.That(clone.Columns.Is("Email", "Domain"), Is.True);
            Assert.That(clone.IsUnique, Is.True);
            Assert.That(clone.IsPrimaryKey, Is.False);
            Assert.That(clone.WhereExpression, Is.EqualTo("Status = 'active'"));
            Assert.That(clone.ExpressionColumns, Is.Not.Null);
            Assert.That(clone.ExpressionColumns![0], Is.EqualTo("LOWER(Email)"));
            Assert.That(clone.IncludeColumns, Is.Not.Null);
            Assert.That(clone.IncludeColumns!.Count, Is.EqualTo(2));
            Assert.That(clone.ColumnDescending, Is.Not.Null);
            Assert.That(clone.ColumnDescending![1], Is.True);
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

            Assert.That(clone.Name, Is.EqualTo("idx_users_email"));
            Assert.That(clone.TableName, Is.EqualTo("Users"));
            Assert.That(clone.Columns.Is("Email", "Domain"), Is.True);
            Assert.That(clone.IsUnique, Is.True);
            Assert.That(clone.IsPrimaryKey, Is.False);
            Assert.That(clone.WhereExpression, Is.EqualTo("Status = 'active'"));
            Assert.That(clone.ExpressionColumns, Is.Not.Null);
            Assert.That(clone.IncludeColumns, Is.Not.Null);
            Assert.That(clone.ColumnDescending, Is.Not.Null);
        }

        [Test]
        public void MemoryPackCloneWithNullsTest()
        {
            var definition = new DefinitionIndex
            {
                Name = "idx_simple",
                TableName = "Table",
                Columns = new[] { "Col" }
            };

            var clone = definition.MemoryPackClone();

            Assert.That(clone, Is.Not.Null);
            Assert.That(clone.WhereExpression, Is.Null);
            Assert.That(clone.ExpressionColumns, Is.Null);
            Assert.That(clone.IncludeColumns, Is.Null);
            Assert.That(clone.ColumnDescending, Is.Null);
        }

        #endregion

        #region ToString Tests

        [Test]
        public void ToStringSimpleIndexTest()
        {
            var definition = new DefinitionIndex
            {
                Name = "idx_users_name",
                TableName = "Users",
                Columns = new[] { "Name" }
            };

            Assert.That(definition.ToString(), Is.EqualTo("INDEX idx_users_name ON Users (Name)"));
        }

        [Test]
        public void ToStringUniqueIndexTest()
        {
            var definition = new DefinitionIndex
            {
                Name = "idx_users_email",
                TableName = "Users",
                Columns = new[] { "Email" },
                IsUnique = true
            };

            Assert.That(definition.ToString(), Is.EqualTo("UNIQUE INDEX idx_users_email ON Users (Email)"));
        }

        [Test]
        public void ToStringCompositeIndexTest()
        {
            var definition = new DefinitionIndex
            {
                Name = "idx_orders_customer_date",
                TableName = "Orders",
                Columns = new[] { "CustomerId", "OrderDate" }
            };

            Assert.That(definition.ToString(), Is.EqualTo("INDEX idx_orders_customer_date ON Orders (CustomerId, OrderDate)"));
        }

        [Test]
        public void ToStringWithDescendingTest()
        {
            var definition = new DefinitionIndex
            {
                Name = "idx_orders_date",
                TableName = "Orders",
                Columns = new[] { "CustomerId", "OrderDate" },
                ColumnDescending = new[] { false, true }
            };

            Assert.That(definition.ToString(), Is.EqualTo("INDEX idx_orders_date ON Orders (CustomerId, OrderDate DESC)"));
        }

        [Test]
        public void ToStringCoveringIndexTest()
        {
            var definition = new DefinitionIndex
            {
                Name = "idx_orders_customer",
                TableName = "Orders",
                Columns = new[] { "CustomerId" },
                IncludeColumns = new[] { "OrderDate", "TotalAmount" }
            };

            Assert.That(definition.ToString(), Is.EqualTo("INDEX idx_orders_customer ON Orders (CustomerId) INCLUDE (OrderDate, TotalAmount)"));
        }

        [Test]
        public void ToStringPartialIndexTest()
        {
            var definition = new DefinitionIndex
            {
                Name = "idx_orders_active",
                TableName = "Orders",
                Columns = new[] { "OrderDate" },
                WhereExpression = "Status = 'active'"
            };

            Assert.That(definition.ToString(), Is.EqualTo("INDEX idx_orders_active ON Orders (OrderDate) WHERE Status = 'active'"));
        }

        [Test]
        public void ToStringExpressionIndexTest()
        {
            var definition = new DefinitionIndex
            {
                Name = "idx_users_email_lower",
                TableName = "Users",
                Columns = new[] { "Email" },
                ExpressionColumns = new[] { "LOWER(Email)" }
            };

            Assert.That(definition.ToString(), Is.EqualTo("INDEX idx_users_email_lower ON Users (LOWER(Email))"));
        }

        #endregion

        #region Helper Methods Tests

        [Test]
        public void IsColumnDescendingReturnsCorrectValueTest()
        {
            var definition = new DefinitionIndex
            {
                Name = "idx_test",
                TableName = "Test",
                Columns = new[] { "A", "B", "C" },
                ColumnDescending = new[] { false, true, false }
            };

            Assert.That(definition.IsColumnDescending(0), Is.False);
            Assert.That(definition.IsColumnDescending(1), Is.True);
            Assert.That(definition.IsColumnDescending(2), Is.False);
            Assert.That(definition.IsColumnDescending(3), Is.False); // Out of bounds
        }

        [Test]
        public void IsColumnDescendingWithNullArrayTest()
        {
            var definition = new DefinitionIndex
            {
                Name = "idx_test",
                TableName = "Test",
                Columns = new[] { "A", "B" }
            };

            Assert.That(definition.IsColumnDescending(0), Is.False);
            Assert.That(definition.IsColumnDescending(1), Is.False);
        }

        [Test]
        public void GetColumnExpressionReturnsCorrectValueTest()
        {
            var definition = new DefinitionIndex
            {
                Name = "idx_test",
                TableName = "Test",
                Columns = new[] { "A", "B" },
                ExpressionColumns = new string?[] { null, "LOWER(B)" }
            };

            Assert.That(definition.GetColumnExpression(0), Is.Null);
            Assert.That(definition.GetColumnExpression(1), Is.EqualTo("LOWER(B)"));
            Assert.That(definition.GetColumnExpression(2), Is.Null); // Out of bounds
        }

        [Test]
        public void GetColumnExpressionWithNullArrayTest()
        {
            var definition = new DefinitionIndex
            {
                Name = "idx_test",
                TableName = "Test",
                Columns = new[] { "A" }
            };

            Assert.That(definition.GetColumnExpression(0), Is.Null);
        }

        #endregion

        #region Computed Properties Tests

        [Test]
        public void IsFilteredTrueWhenHasWhereExpressionTest()
        {
            var definition = new DefinitionIndex
            {
                Name = "idx_test",
                TableName = "Test",
                Columns = new[] { "A" },
                WhereExpression = "B > 0"
            };

            Assert.That(definition.IsFiltered, Is.True);
        }

        [Test]
        public void IsFilteredFalseWhenNoWhereExpressionTest()
        {
            var definition = new DefinitionIndex
            {
                Name = "idx_test",
                TableName = "Test",
                Columns = new[] { "A" }
            };

            Assert.That(definition.IsFiltered, Is.False);
        }

        [Test]
        public void HasExpressionsTrueWhenHasNonNullExpressionTest()
        {
            var definition = new DefinitionIndex
            {
                Name = "idx_test",
                TableName = "Test",
                Columns = new[] { "A" },
                ExpressionColumns = new string?[] { "LOWER(A)" }
            };

            Assert.That(definition.HasExpressions, Is.True);
        }

        [Test]
        public void HasExpressionsFalseWhenAllExpressionsNullTest()
        {
            var definition = new DefinitionIndex
            {
                Name = "idx_test",
                TableName = "Test",
                Columns = new[] { "A" },
                ExpressionColumns = new string?[] { null }
            };

            Assert.That(definition.HasExpressions, Is.False);
        }

        [Test]
        public void IsCoveringTrueWhenHasIncludeColumnsTest()
        {
            var definition = new DefinitionIndex
            {
                Name = "idx_test",
                TableName = "Test",
                Columns = new[] { "A" },
                IncludeColumns = new[] { "B", "C" }
            };

            Assert.That(definition.IsCovering, Is.True);
        }

        [Test]
        public void IsCoveringFalseWhenNoIncludeColumnsTest()
        {
            var definition = new DefinitionIndex
            {
                Name = "idx_test",
                TableName = "Test",
                Columns = new[] { "A" }
            };

            Assert.That(definition.IsCovering, Is.False);
        }

        #endregion

        #region Helpers

        private static DefinitionIndex CreateFullDefinition()
        {
            return new DefinitionIndex
            {
                Name = "idx_users_email",
                TableName = "Users",
                Columns = new[] { "Email", "Domain" },
                IsUnique = true,
                IsPrimaryKey = false,
                WhereExpression = "Status = 'active'",
                ExpressionColumns = new string?[] { "LOWER(Email)", null },
                IncludeColumns = new[] { "Name", "CreatedAt" },
                ColumnDescending = new[] { false, true }
            };
        }

        #endregion
    }
}
