using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Database.Context;

/// <summary>
/// Context for trigger execution with access to OLD and NEW row values.
/// </summary>
public sealed class ContextTrigger : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
    {
        if(modelBase is not ContextTrigger other)
            return false;

        return OldRow.Check(other.OldRow)
               && NewRow.Check(other.NewRow)
               && Cancel.Is(other.Cancel)
               && TriggerName.Is(other.TriggerName);
    }

    public override ContextTrigger Clone()
    {
        return new ContextTrigger
        {
            OldRow = OldRow,
            NewRow = NewRow,
            Cancel = Cancel,
            TriggerName = TriggerName
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// The old row values (before UPDATE or DELETE). Null for INSERT triggers.
    /// </summary>
    public WitSqlRow? OldRow { get; set; }
    
    /// <summary>
    /// The new row values (after INSERT or UPDATE). Null for DELETE triggers.
    /// Modifiable in BEFORE triggers.
    /// </summary>
    public WitSqlRow? NewRow { get; set; }
    
    /// <summary>
    /// Set to true in BEFORE triggers to cancel the operation.
    /// </summary>
    public bool Cancel { get; set; }
    
    /// <summary>
    /// The name of the trigger being executed.
    /// </summary>
    public string? TriggerName { get; set; }

    #endregion
}