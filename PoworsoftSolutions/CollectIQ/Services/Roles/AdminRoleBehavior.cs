using CollectIQ.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CollectIQ.Services.Roles
{
    public class AdminRoleBehavior : IUserRoleBehavior
    {
        public string Role => "Admin";

        public bool CanDeleteCards => true;
        public bool CanAddCards => true;
        public bool CanEditCollections => true;
        public bool CanAccessAdminTools => true;
    }

}
