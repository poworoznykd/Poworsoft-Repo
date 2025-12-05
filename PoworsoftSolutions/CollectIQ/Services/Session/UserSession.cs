using CollectIQ.Interfaces;
using CollectIQ.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CollectIQ.Services.Session
{
    public static class UserSession
    {
        public static UserProfile? CurrentUser { get; set; }
        public static IUserRoleBehavior? CurrentRoleBehavior { get; set; }
    }
}

