using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BulkSMPosting.VM
{
    public class LosCodeExceptionLogVM
    {
        public string ExeSource { get; set; }
        public string ExeType { get; set; }
        public string ExeMessage { get; set; }
        public string ExeStackTrace { get; set; }
        public string InnerExeSource { get; set; }
        public string InnerExeType { get; set; }
        public string InnerExeMessage { get; set; }
        public string InnerExeStackTrace { get; set; }
        public DateTime CreationDate { get; set; }
    }
}
