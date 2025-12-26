using Microsoft.Build.Framework;
using OutWit.Database.Core.Builder;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Definitions;
using OutWit.Database.Interfaces;
using OutWit.Database.Parser;
using OutWit.Database.Schema;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OutWit.Database.Context;
using OutWit.Database.Parser.Statements;

namespace OutWit.Database
{
    public class WitSqlEngine : IDisposable
    {
        #region Fields

        private readonly WitDatabase m_database;
        private readonly SchemaCatalog m_schema;
        private readonly bool m_ownsStore;
        private ITransaction? m_currentTransaction;

        #endregion

        #region Functions

        public WitSqlEngine(WitDatabase database, bool ownsStore = false)
        {
            m_database = database;
            m_schema = new SchemaCatalog(database.Store);
            m_ownsStore = ownsStore;
        }

        #endregion

        public DefinitionTable? GetTable(string tableName)
        {
            return m_schema.GetTable(tableName);
        }


        #region IDisposable

        public void Dispose()
        {
            m_currentTransaction?.Dispose();

            if (m_ownsStore)
                m_database.Dispose();
        }

        #endregion
    }
}
