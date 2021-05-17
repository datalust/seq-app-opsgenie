using System;
using System.Collections.Generic;
using System.Text;

namespace Seq.App.Opsgenie
{
    enum ResponderType
    {
        Team,
        User,
        Escalation,
        Schedule
    }

    class Responders
    {
        public string Name { get; set; }
        public ResponderType Type { get; set; }
    }
}
