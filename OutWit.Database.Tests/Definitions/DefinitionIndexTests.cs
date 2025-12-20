using NUnit.Framework.Legacy;
using OutWit.Common.NUnit;
using OutWit.Database.Definitions;
using System;
using System.Collections.Generic;
using System.Text;
using OutWit.Common.Collections;
using OutWit.Common.Utils;

namespace OutWit.Database.Tests.Definitions
{
    [TestFixture]
    public class DefinitionIndexTests
    {
        [Test]
        public void ConstructorTest()
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

            definition = new DefinitionIndex
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
        public void IsTest()
        {
            var definition = new DefinitionIndex
            {
                Name = "idx_users_email",
                TableName = "Users",
                Columns = new[] { "Email", "Domain" },
                IsUnique = true,
                IsPrimaryKey = false
            };

            Assert.That(definition, Was.EqualTo(definition.Clone()));
            Assert.That(definition, Was.Not.EqualTo(definition.With(idx => idx.Name, "idx_users_name")));
            Assert.That(definition, Was.Not.EqualTo(definition.With(idx => idx.TableName, "Orders")));
            Assert.That(definition, Was.Not.EqualTo(definition.With(idx => idx.Columns, new[] { "Email" })));
            Assert.That(definition, Was.Not.EqualTo(definition.With(idx => idx.IsUnique, false)));
            Assert.That(definition, Was.Not.EqualTo(definition.With(idx => idx.IsPrimaryKey, true)));
        }

        [Test]
        public void CloneTest()
        {
            var definition = new DefinitionIndex
            {
                Name = "idx_users_email",
                TableName = "Users",
                Columns = new[] { "Email", "Domain" },
                IsUnique = true,
                IsPrimaryKey = false
            };

            var clone = definition.Clone();

            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(definition));

            Assert.That(clone.Name, Is.EqualTo("idx_users_email"));
            Assert.That(clone.TableName, Is.EqualTo("Users"));
            Assert.That(clone.Columns.Is("Email", "Domain"), Is.True);
            Assert.That(clone.IsUnique, Is.True);
            Assert.That(clone.IsPrimaryKey, Is.False);
        }

        [Test]
        public void JsonCloneTest()
        {
            var definition = new DefinitionIndex
            {
                Name = "idx_users_email",
                TableName = "Users",
                Columns = new[] { "Email", "Domain" },
                IsUnique = true,
                IsPrimaryKey = false
            };

            var clone = definition.Clone();

            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(definition));

            Assert.That(clone.Name, Is.EqualTo("idx_users_email"));
            Assert.That(clone.TableName, Is.EqualTo("Users"));
            Assert.That(clone.Columns.Is("Email", "Domain"), Is.True);
            Assert.That(clone.IsUnique, Is.True);
            Assert.That(clone.IsPrimaryKey, Is.False);
        }
    }
}
