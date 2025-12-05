using CollectIQ.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CollectIQ.Services.Roles
{
    public class GuestRoleBehavior : IUserRoleBehavior
    {
        public string Role => "Guest";

        public bool CanDeleteCards => false;
        public bool CanAddCards => false;
        public bool CanEditCollections => false;
        public bool CanAccessAdminTools => false;
    }

}
