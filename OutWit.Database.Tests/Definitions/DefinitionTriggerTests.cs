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
    public class DefinitionTriggerTests
    {
        [Test]
        public void ConstructorTest()
        {
            var definition = new DefinitionTrigger
            {
                Name = "trg_users_insert",
                TableName = "Users",
                Time = TriggerTime.Before,
                Event = TriggerEvent.Insert,
                Body = "BEGIN SELECT 1; END"
            };

            Assert.That(definition.Name, Is.EqualTo("trg_users_insert"));
            Assert.That(definition.TableName, Is.EqualTo("Users"));
            Assert.That(definition.Time, Is.EqualTo(TriggerTime.Before));
            Assert.That(definition.Event, Is.EqualTo(TriggerEvent.Insert));
            Assert.That(definition.Body, Is.EqualTo("BEGIN SELECT 1; END"));
            Assert.That(definition.UpdateColumns, Is.Null);
            Assert.That(definition.ForEachRow, Is.False);
            Assert.That(definition.WhenCondition, Is.Null);

            definition = new DefinitionTrigger
            {
                Name = "trg_users_update",
                TableName = "Users",
                Time = TriggerTime.After,
                Event = TriggerEvent.Update,
                UpdateColumns = ["Name", "Email"],
                ForEachRow = true,
                WhenCondition = "NEW.Name <> OLD.Name",
                Body = "BEGIN UPDATE AuditLog SET UpdatedAt = NOW(); END"
            };

            Assert.That(definition.Name, Is.EqualTo("trg_users_update"));
            Assert.That(definition.TableName, Is.EqualTo("Users"));
            Assert.That(definition.Time, Is.EqualTo(TriggerTime.After));
            Assert.That(definition.Event, Is.EqualTo(TriggerEvent.Update));
            Assert.That(definition.UpdateColumns.Is("Name", "Email"), Is.True);
            Assert.That(definition.ForEachRow, Is.True);
            Assert.That(definition.WhenCondition, Is.EqualTo("NEW.Name <> OLD.Name"));
            Assert.That(definition.Body, Is.EqualTo("BEGIN UPDATE AuditLog SET UpdatedAt = NOW(); END"));
        }

        [Test]
        public void IsTest()
        {
            var definition = new DefinitionTrigger
            {
                Name = "trg_users_update",
                TableName = "Users",
                Time = TriggerTime.After,
                Event = TriggerEvent.Update,
                UpdateColumns = ["Name", "Email"],
                ForEachRow = true,
                WhenCondition = "NEW.Name <> OLD.Name",
                Body = "BEGIN UPDATE AuditLog SET UpdatedAt = NOW(); END"
            };

            Assert.That(definition, Was.EqualTo(definition.Clone()));
            Assert.That(definition, Was.Not.EqualTo(definition.With(trg => trg.Name, "trg_users_delete")));
            Assert.That(definition, Was.Not.EqualTo(definition.With(trg => trg.TableName, "Orders")));
            Assert.That(definition, Was.Not.EqualTo(definition.With(trg => trg.Time, TriggerTime.Before)));
            Assert.That(definition, Was.Not.EqualTo(definition.With(trg => trg.Event, TriggerEvent.Delete)));
            Assert.That(definition, Was.Not.EqualTo(definition.With(trg => trg.UpdateColumns, ["Email"])));
            Assert.That(definition, Was.Not.EqualTo(definition.With(trg => trg.ForEachRow, false)));
            Assert.That(definition, Was.Not.EqualTo(definition.With(trg => trg.WhenCondition, "NEW.Email <> OLD.Email")));
            Assert.That(definition, Was.Not.EqualTo(definition.With(trg => trg.Body, "BEGIN SELECT 1; END")));
        }

        [Test]
        public void CloneTest()
        {
            var definition = new DefinitionTrigger
            {
                Name = "trg_users_update",
                TableName = "Users",
                Time = TriggerTime.After,
                Event = TriggerEvent.Update,
                UpdateColumns = new[] { "Name", "Email" },
                ForEachRow = true,
                WhenCondition = "NEW.Name <> OLD.Name",
                Body = "BEGIN UPDATE AuditLog SET UpdatedAt = NOW(); END"
            };

            var clone = definition.Clone();

            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(definition));

            Assert.That(clone.Name, Is.EqualTo("trg_users_update"));
            Assert.That(clone.TableName, Is.EqualTo("Users"));
            Assert.That(clone.Time, Is.EqualTo(TriggerTime.After));
            Assert.That(clone.Event, Is.EqualTo(TriggerEvent.Update));
            Assert.That(clone.UpdateColumns.Is("Name", "Email"), Is.True);
            Assert.That(clone.ForEachRow, Is.True);
            Assert.That(clone.WhenCondition, Is.EqualTo("NEW.Name <> OLD.Name"));
            Assert.That(clone.Body, Is.EqualTo("BEGIN UPDATE AuditLog SET UpdatedAt = NOW(); END"));
        }

        [Test]
        public void JsonCloneTest()
        {
            var definition = new DefinitionTrigger
            {
                Name = "trg_users_update",
                TableName = "Users",
                Time = TriggerTime.After,
                Event = TriggerEvent.Update,
                UpdateColumns = new[] { "Name", "Email" },
                ForEachRow = true,
                WhenCondition = "NEW.Name <> OLD.Name",
                Body = "BEGIN UPDATE AuditLog SET UpdatedAt = NOW(); END"
            };

            var clone = definition.Clone();

            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(definition));

            Assert.That(clone.Name, Is.EqualTo("trg_users_update"));
            Assert.That(clone.TableName, Is.EqualTo("Users"));
            Assert.That(clone.Time, Is.EqualTo(TriggerTime.After));
            Assert.That(clone.Event, Is.EqualTo(TriggerEvent.Update));
            Assert.That(clone.UpdateColumns.Is("Name", "Email"), Is.True);
            Assert.That(clone.ForEachRow, Is.True);
            Assert.That(clone.WhenCondition, Is.EqualTo("NEW.Name <> OLD.Name"));
            Assert.That(clone.Body, Is.EqualTo("BEGIN UPDATE AuditLog SET UpdatedAt = NOW(); END"));
        }
    }
}
