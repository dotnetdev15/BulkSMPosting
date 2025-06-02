using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BulkSmPosting.Logics.BLL
{
    public class BaseBLL : IDisposable
    {
        private bool disposed = false;

        protected virtual void Dispose(bool disposing)
        {
            this.disposed = true;
        }

        public void Dispose()
        {
            Dispose(disposed);
            GC.SuppressFinalize(this);
        }
    }
}
