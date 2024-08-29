using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UtilityTracking.GeorgiaPower
{
    public record GeorgiaPowerCredentials : UserCredentials
    {
        public GeorgiaPowerCredentials(string Username, string Password) : base(Username, Password)
        {
        }
    }
}
