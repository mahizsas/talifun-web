﻿using System;
using System.Collections.Generic;
using System.IO;

namespace Talifun.Web.CssSprite
{
    public class CssSpriteCacheItem
    {
        public FileInfo ImageOutputPath { get; set; }
        public FileInfo CssOutputPath { get; set; }
        public Uri SpriteImageUrl { get; set; }
        public IEnumerable<ImageFile> ImageFiles { get; set; }
    }
}
