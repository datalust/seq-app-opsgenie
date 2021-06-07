using System;
using System.Collections.Generic;
using System.Text;

namespace Seq.App.Opsgenie
{
    public enum Priority
    {
        P1 = 1,
        P2 = 2,
        P3 = 3,
        P4 = 4,
        P5 = 5
    }
    public class Priorities
    {
        public string Name;
        public Priority Priority;
    }
}
