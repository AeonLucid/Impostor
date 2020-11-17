﻿using Impostor.Api.Innersloth;
using Impostor.Api.Net.Messages;

namespace Impostor.Api.Net.Inner.Objects
{
    public interface ITaskInfo
    {
        uint TaskIndex { get; }

        TaskTypes Type { get; }

        bool Complete { get; }
    }
}
