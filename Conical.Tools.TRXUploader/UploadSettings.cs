using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TRXUploader
{
    internal class UploadSettings
    {
        public string Server { get; set; }
        public string Token { get; set; }
        public string Product { get; set; }
        public string TestRunSetName { get; set; } = "Unit Tests";
        public string TestRunSetDescription { get; set; } = "Unit Tests";
        public string TestRunType { get; set; }
    }
}
