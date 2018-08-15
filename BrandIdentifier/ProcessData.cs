using System;
using System.Collections.Generic;
using System.Text;

namespace BrandIdentifier
{
    public class ExtractProcessData
    {
       public string videoid { get; set; }

        public string filename { get; set; }

        public string url { get; set; }

        public string reqUri { get; set; }
        public string PathAndQuery { get; set; }

        public string startTime { get; set; }

        public string endTime { get; set; }

        public int framesStart { get; set; }

        public int framesEnd { get; set; }
    }


}
