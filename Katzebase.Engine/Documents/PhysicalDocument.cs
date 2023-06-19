﻿using Katzebase.PublicLibrary.Payloads;

namespace Katzebase.Engine.Documents
{
    /// <summary>
    /// This is the page document that is physically written to the disk by
    ///     virture of being contained in the collection in PhysicalDocumentPage
    /// </summary>
    [Serializable]
    public class PhysicalDocument
    {
        public string Content { get; set; } = string.Empty;
        public uint Id { get; set; }
        public DateTime Created { get; set; }
        public DateTime Modfied { get; set; }

        public PhysicalDocument Clone()
        {
            return new PhysicalDocument
            {
                Id = Id,
                Content = Content,
                Created = Created,
                Modfied = Modfied
            };
        }
    }
}
