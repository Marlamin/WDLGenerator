namespace WDLGenerator
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var inputDir = "input";

            if(!string.IsNullOrEmpty(args[0]))
            {
                inputDir = args[0];
            }
            else
            {
                if (!Directory.Exists("input"))
                {
                    Directory.CreateDirectory("input");
                }
            }

            var mapName = "";

            var adtDict = new Dictionary<(byte, byte), string>();

            foreach (var file in Directory.GetFiles(inputDir, "*.adt"))
            {
                if (file.EndsWith("_lod.adt") || file.EndsWith("obj0.adt") || file.EndsWith("obj1.adt") || file.EndsWith("tex0.adt"))
                    continue;

                var filename = Path.GetFileName(file);
                var splitName = filename.Split('_');

                // bit dirty but yolo
                var x = byte.Parse(splitName[splitName.Length - 2]);
                var y = byte.Parse(splitName[splitName.Length - 1].Replace(".adt", ""));
                adtDict[(x, y)] = filename;

                mapName = filename.Replace("_" + x + "_" + y + ".adt", "");
            }

            if(adtDict.Count == 0)
            {
                Console.WriteLine("No ADTs found in input folder.");
                Console.WriteLine("Press enter to exit");
                Console.ReadLine();
                return;
            }

            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                // MVER
                bw.Write("REVM".ToCharArray());
                bw.Write((uint)4);
                bw.Write((uint)18);

                // Legion+ chunks
                // MLDD
                bw.Write("DDLM".ToCharArray());
                bw.Write((uint)0);

                // MLDX
                bw.Write("XDLM".ToCharArray());
                bw.Write((uint)0);

                // MLMD
                bw.Write("DMLM".ToCharArray());
                bw.Write((uint)0);

                // MLMX
                bw.Write("XMLM".ToCharArray());
                bw.Write((uint)0);

                // WotLK chunks
                //// MWMO
                //bw.Write("OMWM".ToCharArray());
                //bw.Write((uint)0);

                //// MWID
                //bw.Write("DIWM".ToCharArray());
                //bw.Write((uint)0);

                //// MODF
                //bw.Write("FDOM".ToCharArray());
                //bw.Write((uint)0);

                // MOAF
                bw.Write("FOAM".ToCharArray());
                bw.Write((uint)16384);

                var moafOffset = ms.Position;
                for (int x = 0; x < 64; x++)
                {
                    for (int y = 0; y < 64; y++)
                    {
                        bw.Write((uint)0);
                    }
                }

                // For each ADT we write a MARE, MAOE, MAHO
                // Order might matter but for now just run through dict
                foreach (var adt in adtDict)
                {
                    var coord = adt.Key;
                    var filename = adt.Value;

                    var adtReader = new WoWFormatLib.FileReaders.ADTReader();
                    adtReader.ReadRootFile(File.OpenRead(Path.Combine(inputDir, filename)), 0);

                    // Marty, we got to go back and update MOAF!
                    var currentOffset = ms.Position;
                    ms.Position = moafOffset + (coord.Item2 * 64 + coord.Item1) * 4;
                    bw.Write((uint)currentOffset);
                    ms.Position = currentOffset;

                    var heights = new float[256][];
                    for (var i = 0; i < 16; ++i)
                    {
                        for (var j = 0; j < 16; ++j)
                        {
                            heights[i * 16 + j] = adtReader.adtfile.chunks[i * 16 + j].vertices.vertices;

                            for (var k = 0; k < heights[i * 16 + j].Length; ++k)
                                heights[i * 16 + j][k] += adtReader.adtfile.chunks[i * 16 + j].header.position.Z;
                        }
                    }

                    var retValue = new short[17 * 17 + 16 * 16];
                    const float stepSize = Metrics.TileSize / 16.0f;

                    // MARE
                    bw.Write("ERAM".ToCharArray());
                    bw.Write((uint)1090);

                    var mareStartPos = ms.Position;

                    // Outer
                    // By Luzifix: https://github.com/Luzifix/ADTConvert
                    for (var i = 0; i < 17; ++i)
                    {
                        for (var j = 0; j < 17; ++j)
                        {
                            var posx = j * stepSize;
                            var posy = i * stepSize;

                            bw.Write((short)
                                Math.Min(
                                    Math.Max(
                                        Math.Round(GetLandHeight(heights, posx, posy)),
                                        short.MinValue),
                                    short.MaxValue));
                        }
                    }

                    // Inner
                    // By Luzifix: https://github.com/Luzifix/ADTConvert
                    for (var i = 0; i < 16; ++i)
                    {
                        for (var j = 0; j < 16; ++j)
                        {
                            var posx = j * stepSize;
                            var posy = i * stepSize;
                            posx += stepSize / 2.0f;
                            posy += stepSize / 2.0f;

                            bw.Write((short)
                                Math.Min(
                                    Math.Max(
                                        Math.Round(GetLandHeight(heights, posx, posy)),
                                        short.MinValue),
                                    short.MaxValue));
                        }
                    }


                    // MAOE, optional
                    // Something something ocean, just FF's for now.
                    //bw.Write("EOAM".ToCharArray());
                    //bw.Write((uint)32);
                    //for (int i = 0; i < 16; i++)
                    //{
                    //    bw.Write((short)-1);
                    //}

                    // MAHO
                    // MapAreaHOles, we're just not going to write this out for now.
                    bw.Write("OHAM".ToCharArray());
                    bw.Write((uint)32);
                    for (int i = 0; i < 16; i++)
                    {
                        bw.Write((ushort)0);
                    }
                    // end foreach
                }

                // write out the file
                if (!Directory.Exists("output"))
                        Directory.CreateDirectory("output");

                File.WriteAllBytes(Path.Combine("output", mapName + ".wdl"), ms.ToArray());

                Console.WriteLine("Press enter to exit");
                Console.ReadLine();
            }
        }

        // By Luzifix: https://github.com/Luzifix/ADTConvert
        private static float GetLandHeight(float[][] heights, float x, float y)
        {
            var cx = (int)Math.Floor(x / Metrics.ChunkSize);
            var cy = (int)Math.Floor(y / Metrics.ChunkSize);
            cx = Math.Min(Math.Max(cx, 0), 15);
            cy = Math.Min(Math.Max(cy, 0), 15);

            if (heights[cy * 16 + cx] == null)
                return 0;

            x -= cx * Metrics.ChunkSize;
            y -= cy * Metrics.ChunkSize;

            var row = (int)(y / (Metrics.UnitSize * 0.5f) + 0.5f);
            var col = (int)((x - Metrics.UnitSize * 0.5f * (row % 2)) / Metrics.UnitSize + 0.5f);

            if (row < 0 || col < 0 || row > 16 || col > (((row % 2) != 0) ? 8 : 9))
                return 0;

            return heights[cy * 16 + cx][17 * (row / 2) + (((row % 2) != 0) ? 9 : 0) + col];
        }
    }
    static class Metrics
    {
        public const float TileSize = 533.0f + 1.0f / 3.0f;
        public const float ChunkSize = TileSize / 16.0f;
        public const float UnitSize = ChunkSize / 8.0f;
        public const float MapMidPoint = 32.0f * TileSize;
        public const float ChunkRadius = 1.4142135f * ChunkSize;
    }
}
