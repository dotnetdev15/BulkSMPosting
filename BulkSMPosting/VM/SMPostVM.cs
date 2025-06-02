using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BulkSMPosting.VM
{
    public class SMPostVM
    {
        public string PostSM { get; set; }
        public string SMCode { get; set; }
        public string ParytCode { get; set; }
        public string CustName { get; set; }
        public string LoanAmt { get; set; }
        public string AheadsMain { get; set; }
        public string Duration { get; set; }
        public string Creator { get; set; }
        public string FiCode { get; set; }
        public Int64 FiId { get; set; }
        public DateTime? PaymentDate { get; set; }
    }
}
