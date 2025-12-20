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
    public class DefinitionViewTests
    {
        [Test]
        public void ConstructorTest()
        {
            var definition = new DefinitionView
            {
                Name = "1",
                SelectSql = "2"
            };

            Assert.That(definition.Name, Is.EqualTo("1"));
            Assert.That(definition.SelectSql, Is.EqualTo("2"));
            Assert.That(definition.ColumnAliases, Is.Null);

            definition = new DefinitionView
            {
                Name = "1",
                SelectSql = "2",
                ColumnAliases = ["3", "4"]
            };

            Assert.That(definition.Name, Is.EqualTo("1"));
            Assert.That(definition.SelectSql, Is.EqualTo("2"));
            Assert.That(definition.ColumnAliases.Is("3", "4"), Is.True);
        }

        [Test]
        public void IsTest()
        {
            var definition = new DefinitionView
            {
                Name = "1",
                SelectSql = "2",
                ColumnAliases = ["3", "4"]
            };

            Assert.That(definition, Was.EqualTo(definition.Clone()));
            Assert.That(definition, Was.Not.EqualTo(definition.With(view => view.Name, "2")));
            Assert.That(definition, Was.Not.EqualTo(definition.With(view => view.SelectSql, "3")));
            Assert.That(definition, Was.Not.EqualTo(definition.With(view => view.ColumnAliases, ["4"])));
        }

        [Test]
        public void CloneTest()
        {
            var definition = new DefinitionView
            {
                Name = "1",
                SelectSql = "2",
                ColumnAliases = ["3", "4"]
            };

            var clone = definition.Clone();

            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(definition));

            Assert.That(clone.Name, Is.EqualTo("1"));
            Assert.That(clone.SelectSql, Is.EqualTo("2"));
            Assert.That(clone.ColumnAliases.Is("3", "4"), Is.True);
        }

        [Test]
        public void JsonCloneTest()
        {
            var definition = new DefinitionView
            {
                Name = "1",
                SelectSql = "2",
                ColumnAliases = ["3", "4"]
            };

            var clone = definition.Clone();

            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(definition));

            Assert.That(clone.Name, Is.EqualTo("1"));
            Assert.That(clone.SelectSql, Is.EqualTo("2"));
            Assert.That(clone.ColumnAliases.Is("3", "4"), Is.True);
        }

    }
}
