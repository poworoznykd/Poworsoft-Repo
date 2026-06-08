using CollectIQ.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CollectIQ.Services.Roles
{
    public class RegularRoleBehavior : IUserRoleBehavior
    {
        public string Role => "Regular";

        public bool CanDeleteCards => false;
        public bool CanAddCards => true;
        public bool CanEditCollections => true;
        public bool CanAccessAdminTools => false;
    }

}
