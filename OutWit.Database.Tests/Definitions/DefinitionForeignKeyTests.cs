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
    public class DefinitionForeignKeyTests
    {
        [Test]
        public void ConstructorTest()
        {
            var definition = new DefinitionForeignKey
            {
                Columns = new[] { "Id" },
                ForeignTable = "OtherTable"
            };

            Assert.That(definition.Columns.Is("Id"), Is.True);
            Assert.That(definition.ForeignTable, Is.EqualTo("OtherTable"));
            Assert.That(definition.ForeignColumns, Is.Null);
            Assert.That(definition.OnDelete, Is.EqualTo(ReferenceAction.NoAction));
            Assert.That(definition.OnUpdate, Is.EqualTo(ReferenceAction.NoAction));

            definition = new DefinitionForeignKey
            {
                Columns = new[] { "Id", "Code" },
                ForeignTable = "OtherTable",
                ForeignColumns = new[] { "RefId", "RefCode" },
                OnDelete = ReferenceAction.Cascade,
                OnUpdate = ReferenceAction.SetNull
            };

            Assert.That(definition.Columns.Is("Id", "Code"), Is.True);
            Assert.That(definition.ForeignTable, Is.EqualTo("OtherTable"));
            Assert.That(definition.ForeignColumns.Is("RefId", "RefCode"), Is.True);
            Assert.That(definition.OnDelete, Is.EqualTo(ReferenceAction.Cascade));
            Assert.That(definition.OnUpdate, Is.EqualTo(ReferenceAction.SetNull));
        }

        [Test]
        public void IsTest()
        {
            var definition = new DefinitionForeignKey
            {
                Columns = new[] { "Id", "Code" },
                ForeignTable = "OtherTable",
                ForeignColumns = new[] { "RefId", "RefCode" },
                OnDelete = ReferenceAction.Cascade,
                OnUpdate = ReferenceAction.SetNull
            };

            Assert.That(definition, Was.EqualTo(definition.Clone()));
            Assert.That(definition, Was.Not.EqualTo(definition.With(fk => fk.Columns, new[] { "Id" })));
            Assert.That(definition, Was.Not.EqualTo(definition.With(fk => fk.ForeignTable, "AnotherTable")));
            Assert.That(definition, Was.Not.EqualTo(definition.With(fk => fk.ForeignColumns, new[] { "RefId" })));
            Assert.That(definition, Was.Not.EqualTo(definition.With(fk => fk.OnDelete, ReferenceAction.Restrict)));
            Assert.That(definition, Was.Not.EqualTo(definition.With(fk => fk.OnUpdate, ReferenceAction.SetDefault)));
        }

        [Test]
        public void CloneTest()
        {
            var definition = new DefinitionForeignKey
            {
                Columns = new[] { "Id", "Code" },
                ForeignTable = "OtherTable",
                ForeignColumns = new[] { "RefId", "RefCode" },
                OnDelete = ReferenceAction.Cascade,
                OnUpdate = ReferenceAction.SetNull
            };

            var clone = definition.Clone();

            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(definition));

            Assert.That(clone.Columns.Is("Id", "Code"), Is.True);
            Assert.That(clone.ForeignTable, Is.EqualTo("OtherTable"));
            Assert.That(clone.ForeignColumns.Is("RefId", "RefCode"), Is.True);
            Assert.That(clone.OnDelete, Is.EqualTo(ReferenceAction.Cascade));
            Assert.That(clone.OnUpdate, Is.EqualTo(ReferenceAction.SetNull));
        }

        [Test]
        public void JsonCloneTest()
        {
            var definition = new DefinitionForeignKey
            {
                Columns = new[] { "Id", "Code" },
                ForeignTable = "OtherTable",
                ForeignColumns = new[] { "RefId", "RefCode" },
                OnDelete = ReferenceAction.Cascade,
                OnUpdate = ReferenceAction.SetNull
            };

            var clone = definition.Clone();

            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(definition));

            Assert.That(clone.Columns.Is("Id", "Code"), Is.True);
            Assert.That(clone.ForeignTable, Is.EqualTo("OtherTable"));
            Assert.That(clone.ForeignColumns.Is("RefId", "RefCode"), Is.True);
            Assert.That(clone.OnDelete, Is.EqualTo(ReferenceAction.Cascade));
            Assert.That(clone.OnUpdate, Is.EqualTo(ReferenceAction.SetNull));
        }
    }
}
