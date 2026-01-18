using CollectIQ.Services.Session;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CollectIQ.Interfaces
{
    public interface ISyncService
    {
        Task SyncAsync(UserSession session);
    }

}
