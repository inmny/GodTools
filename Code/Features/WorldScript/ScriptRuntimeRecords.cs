using System;
using System.Collections.Generic;

namespace GodTools.Features.WorldScript;

public sealed class ScriptEventCause
{
    public string ScriptID;
    public string ScriptName;
    public int Step;
}

public sealed class ScriptWriteRecord
{
    public ScriptTargetSample Target;
    public string Key;
    public string Policy;
    public long Before;
    public long After;
    public string Reason;
}

public sealed class ScriptBatchJob
{
    public string ID = Guid.NewGuid().ToString("N");
    public string Definition;
    public string TriggerID;
    public bool Manual;
    public List<ScriptTargetSample> Targets;
    public List<int> Steps;
    public int Step;
    public int Offset;
    public int Spawned;
    public int Applied;
    public int MatchCount;
}
