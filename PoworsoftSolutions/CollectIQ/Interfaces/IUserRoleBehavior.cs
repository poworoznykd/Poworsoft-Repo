using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CollectIQ.Interfaces
{
    public interface IUserRoleBehavior
    {
        string Role { get; }

        bool CanDeleteCards { get; }
        bool CanAddCards { get; }
        bool CanEditCollections { get; }
        bool CanAccessAdminTools { get; }
    }

}
