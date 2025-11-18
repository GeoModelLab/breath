using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace source
{
    /// <summary>
    /// Represents metadata for a single weather data chunk file.
    /// </summary>
    public class ChunkInfo
    {
        /// <summary>Relative file path from data folder (e.g., "TX/chunk_0001-0730_1980-1981.bin")</summary>
        public string File { get; set; }

        /// <summary>Starting year of the chunk</summary>
        public int StartYear { get; set; }

        /// <summary>Day of year (1-366) for the first entry</summary>
        public int StartDayOfYear { get; set; }

        /// <summary>Number of days in this chunk</summary>
        public int NumDays { get; set; }

        /// <summary>Number of spatial coordinates</summary>
        public int NumCoordinates { get; set; }

        /// <summary>File size in bytes</summary>
        public long SizeBytes { get; set; }

        /// <summary>Statistical minimum value (excluding NaN)</summary>
        public float StatMin { get; set; }

        /// <summary>Statistical maximum value (excluding NaN)</summary>
        public float StatMax { get; set; }

        /// <summary>Statistical mean value (excluding NaN)</summary>
        public float StatMean { get; set; }

        /// <summary>Percentage of missing data (NaN values)</summary>
        public float MissingDataPercent { get; set; }

        /// <summary>
        /// Split operation: returns all properties as individual output parameters for vvvv.
        /// Grouped by meaning: file info, temporal info, spatial info, statistics.
        /// </summary>
        public void Split(
            out string file,
            out long sizeBytes,
            out int startYear,
            out int startDayOfYear,
            out int numDays,
            out int numCoordinates,
            out float statMin,
            out float statMax,
            out float statMean,
            out float missingDataPercent)
        {
            file = File;
            sizeBytes = SizeBytes;
            startYear = StartYear;
            startDayOfYear = StartDayOfYear;
            numDays = NumDays;
            numCoordinates = NumCoordinates;
            statMin = StatMin;
            statMax = StatMax;
            statMean = StatMean;
            missingDataPercent = MissingDataPercent;
        }
    }

    /// <summary>
    /// Represents metadata for a single weather attribute (e.g., TX, TN, RR).
    /// </summary>
    public class AttributeInfo
    {
        /// <summary>Attribute name (e.g., "TX", "TN", "RR")</summary>
        public string Name { get; set; }

        /// <summary>Human-readable description</summary>
        public string Description { get; set; }

        /// <summary>Physical unit (e.g., "°C", "mm", "m/s")</summary>
        public string Unit { get; set; }

        /// <summary>Expected minimum value range</summary>
        public float RangeMin { get; set; }

        /// <summary>Expected maximum value range</summary>
        public float RangeMax { get; set; }

        /// <summary>List of all chunks for this attribute</summary>
        public List<ChunkInfo> Chunks { get; set; } = new List<ChunkInfo>();

        /// <summary>
        /// Split operation: returns all properties as individual output parameters for vvvv.
        /// Grouped by meaning: identification, description, range, data chunks.
        /// </summary>
        public void Split(
            out string name,
            out string description,
            out string unit,
            out float rangeMin,
            out float rangeMax,
            out List<ChunkInfo> chunks)
        {
            name = Name;
            description = Description;
            unit = Unit;
            rangeMin = RangeMin;
            rangeMax = RangeMax;
            chunks = Chunks;
        }
    }

    /// <summary>
    /// Represents the complete weather data specification from specification.xml.
    /// </summary>
    public class WeatherDataSpecification
    {
        /// <summary>Specification format version</summary>
        public int Version { get; set; }

        /// <summary>Creation timestamp</summary>
        public DateTime Created { get; set; }

        /// <summary>Data source identifier</summary>
        public string Source { get; set; }

        /// <summary>Converter tool identifier</summary>
        public string Converter { get; set; }

        /// <summary>Total number of spatial coordinates</summary>
        public int NumCoordinates { get; set; }

        /// <summary>Lookup table binary file name</summary>
        public string LookupTableFile { get; set; }

        /// <summary>Grid resolution in degrees</summary>
        public float GridResolution { get; set; }

        /// <summary>Number of longitude grid cells</summary>
        public int NumLonCells { get; set; }

        /// <summary>Number of latitude grid cells</summary>
        public int NumLatCells { get; set; }

        /// <summary>Coordinate ordering scheme</summary>
        public string CoordinateOrdering { get; set; }

        /// <summary>Minimum longitude bound</summary>
        public float MinLongitude { get; set; }

        /// <summary>Maximum longitude bound</summary>
        public float MaxLongitude { get; set; }

        /// <summary>Minimum latitude bound</summary>
        public float MinLatitude { get; set; }

        /// <summary>Maximum latitude bound</summary>
        public float MaxLatitude { get; set; }

        /// <summary>Start year of data coverage</summary>
        public int StartYear { get; set; }

        /// <summary>End year of data coverage</summary>
        public int EndYear { get; set; }

        /// <summary>Total number of days across all chunks</summary>
        public int TotalDays { get; set; }

        /// <summary>Standard chunk size in days</summary>
        public int ChunkSizeDays { get; set; }

        /// <summary>Total number of chunks</summary>
        public int TotalChunks { get; set; }

        /// <summary>Data type identifier (e.g., "float32")</summary>
        public string DataType { get; set; }

        /// <summary>Missing value encoding scheme (e.g., "NaN")</summary>
        public string MissingValueEncoding { get; set; }

        /// <summary>Processing time in seconds</summary>
        public float ProcessingTimeSeconds { get; set; }

        /// <summary>Dictionary of attributes by name</summary>
        public Dictionary<string, AttributeInfo> Attributes { get; set; } = new Dictionary<string, AttributeInfo>();

        /// <summary>
        /// Split operation: returns all properties as individual output parameters for vvvv.
        /// Grouped by meaning: metadata, temporal coverage, spatial grid, data organization, technical details.
        /// </summary>
        public void Split(
            out int version,
            out string source,
            out string converter,
            out DateTime created,
            out float processingTimeSeconds,
            out int startYear,
            out int endYear,
            out int totalDays,
            out int chunkSizeDays,
            out int totalChunks,
            out float minLongitude,
            out float maxLongitude,
            out float minLatitude,
            out float maxLatitude,
            out float gridResolution,
            out int numLonCells,
            out int numLatCells,
            out int numCoordinates,
            out string coordinateOrdering,
            out string lookupTableFile,
            out string dataType,
            out string missingValueEncoding,
            out Dictionary<string, AttributeInfo> attributes)
        {
            version = Version;
            source = Source;
            converter = Converter;
            created = Created;
            processingTimeSeconds = ProcessingTimeSeconds;
            startYear = StartYear;
            endYear = EndYear;
            totalDays = TotalDays;
            chunkSizeDays = ChunkSizeDays;
            totalChunks = TotalChunks;
            minLongitude = MinLongitude;
            maxLongitude = MaxLongitude;
            minLatitude = MinLatitude;
            maxLatitude = MaxLatitude;
            gridResolution = GridResolution;
            numLonCells = NumLonCells;
            numLatCells = NumLatCells;
            numCoordinates = NumCoordinates;
            coordinateOrdering = CoordinateOrdering;
            lookupTableFile = LookupTableFile;
            dataType = DataType;
            missingValueEncoding = MissingValueEncoding;
            attributes = Attributes;
        }
    }

    /// <summary>
    /// Represents loaded chunk data in memory.
    /// Data layout: coordinate-major, time-minor [coord_idx * numDays + day_idx]
    /// </summary>
    public class ChunkData
    {
        /// <summary>Chunk metadata</summary>
        public ChunkInfo Info { get; set; }

        /// <summary>Attribute name</summary>
        public string AttributeName { get; set; }

        /// <summary>Raw float32 data array (coordinate-major, time-minor)</summary>
        public float[] Data { get; set; }

        /// <summary>Number of coordinates</summary>
        public int NumCoordinates => Info.NumCoordinates;

        /// <summary>Number of days</summary>
        public int NumDays => Info.NumDays;

        /// <summary>
        /// Extract time series for a single coordinate.
        /// </summary>
        /// <param name="coordIndex">Coordinate index (0-based)</param>
        /// <returns>Array of float values for all days at this coordinate</returns>
        public float[] GetCoordinateTimeSeries(int coordIndex)
        {
            if (coordIndex < 0 || coordIndex >= NumCoordinates)
                throw new ArgumentOutOfRangeException(nameof(coordIndex),
                    $"Coordinate index must be between 0 and {NumCoordinates - 1}");

            float[] timeSeries = new float[NumDays];
            int offset = coordIndex * NumDays;
            Array.Copy(Data, offset, timeSeries, 0, NumDays);
            return timeSeries;
        }

        /// <summary>
        /// Extract single value at specific coordinate and day.
        /// </summary>
        /// <param name="coordIndex">Coordinate index (0-based)</param>
        /// <param name="dayIndex">Day index within chunk (0-based)</param>
        /// <returns>Float value (may be NaN for missing data)</returns>
        public float GetValue(int coordIndex, int dayIndex)
        {
            if (coordIndex < 0 || coordIndex >= NumCoordinates)
                throw new ArgumentOutOfRangeException(nameof(coordIndex));

            if (dayIndex < 0 || dayIndex >= NumDays)
                throw new ArgumentOutOfRangeException(nameof(dayIndex));

            return Data[coordIndex * NumDays + dayIndex];
        }

        /// <summary>
        /// Split operation: returns all properties as individual output parameters for vvvv.
        /// Grouped by meaning: identification, data array, dimensions, metadata.
        /// </summary>
        public void Split(
            out string attributeName,
            out float[] data,
            out int numCoordinates,
            out int numDays,
            out ChunkInfo info)
        {
            attributeName = AttributeName;
            data = Data;
            numCoordinates = NumCoordinates;
            numDays = NumDays;
            info = Info;
        }
    }

    /// <summary>
    /// Async reader for E-OBS weather data chunks exported by eobs_chunk_exporter.R.
    /// Supports loading binary float32 chunk files and streaming data for GPU processing.
    ///
    /// Note: E-OBS NetCDF files use -99.99 as _FillValue (missing data marker).
    /// This reader automatically converts -99.99 to IEEE 754 NaN when loading data.
    /// </summary>
    public class WeatherChunkReader
    {
        private readonly string _dataFolderPath;
        private readonly WeatherDataSpecification _specification;

        /// <summary>
        /// Gets the loaded weather data specification.
        /// </summary>
        public WeatherDataSpecification Specification => _specification;

        /// <summary>
        /// Initializes a new instance of the WeatherChunkReader.
        /// </summary>
        /// <param name="dataFolderPath">Path to the data folder containing specification.xml and chunk files</param>
        public WeatherChunkReader(string dataFolderPath)
        {
            if (string.IsNullOrEmpty(dataFolderPath))
                throw new ArgumentNullException(nameof(dataFolderPath));

            if (!Directory.Exists(dataFolderPath))
                throw new DirectoryNotFoundException($"Data folder not found: {dataFolderPath}");

            _dataFolderPath = dataFolderPath;

            string specPath = Path.Combine(dataFolderPath, "specification.xml");
            if (!File.Exists(specPath))
                throw new FileNotFoundException($"Specification file not found: {specPath}");

            _specification = ParseSpecification(specPath);
        }

        /// <summary>
        /// Parse the specification.xml file.
        /// </summary>
        private static WeatherDataSpecification ParseSpecification(string xmlPath)
        {
            XDocument doc = XDocument.Load(xmlPath);
            XElement root = doc.Root;

            var spec = new WeatherDataSpecification
            {
                Version = int.Parse(root.Attribute("version").Value),
                Created = DateTime.Parse(root.Attribute("created").Value),
                Source = root.Attribute("source").Value,
                Converter = root.Attribute("converter").Value,
                NumCoordinates = int.Parse(root.Attribute("numCoordinates").Value),
                LookupTableFile = root.Attribute("lookupTableFile").Value,
                GridResolution = float.Parse(root.Attribute("gridResolution").Value),
                NumLonCells = int.Parse(root.Attribute("numLonCells").Value),
                NumLatCells = int.Parse(root.Attribute("numLatCells").Value),
                CoordinateOrdering = root.Attribute("coordinateOrdering").Value,
                MinLongitude = float.Parse(root.Attribute("minLongitude").Value),
                MaxLongitude = float.Parse(root.Attribute("maxLongitude").Value),
                MinLatitude = float.Parse(root.Attribute("minLatitude").Value),
                MaxLatitude = float.Parse(root.Attribute("maxLatitude").Value),
                StartYear = int.Parse(root.Attribute("startYear").Value),
                EndYear = int.Parse(root.Attribute("endYear").Value),
                TotalDays = int.Parse(root.Attribute("totalDays").Value),
                ChunkSizeDays = int.Parse(root.Attribute("chunkSizeDays").Value),
                TotalChunks = int.Parse(root.Attribute("totalChunks").Value),
                DataType = root.Attribute("dataType").Value,
                MissingValueEncoding = root.Attribute("missingValueEncoding").Value,
                ProcessingTimeSeconds = float.Parse(root.Attribute("processingTimeSeconds").Value)
            };

            // Parse attributes
            foreach (var attrElement in root.Elements("Attribute"))
            {
                var attrInfo = new AttributeInfo
                {
                    Name = attrElement.Attribute("name").Value,
                    Description = attrElement.Attribute("description").Value,
                    Unit = attrElement.Attribute("unit").Value,
                    RangeMin = float.Parse(attrElement.Attribute("rangeMin").Value),
                    RangeMax = float.Parse(attrElement.Attribute("rangeMax").Value)
                };

                // Parse bins (chunks)
                foreach (var binElement in attrElement.Elements("Bin"))
                {
                    var chunkInfo = new ChunkInfo
                    {
                        File = binElement.Attribute("file").Value,
                        StartYear = int.Parse(binElement.Attribute("startYear").Value),
                        StartDayOfYear = int.Parse(binElement.Attribute("startDayOfYear").Value),
                        NumDays = int.Parse(binElement.Attribute("numDays").Value),
                        NumCoordinates = int.Parse(binElement.Attribute("numCoordinates").Value),
                        SizeBytes = long.Parse(binElement.Attribute("sizeBytes").Value),
                        StatMin = float.Parse(binElement.Attribute("statMin").Value),
                        StatMax = float.Parse(binElement.Attribute("statMax").Value),
                        StatMean = float.Parse(binElement.Attribute("statMean").Value),
                        MissingDataPercent = float.Parse(binElement.Attribute("missingDataPercent").Value)
                    };

                    attrInfo.Chunks.Add(chunkInfo);
                }

                spec.Attributes[attrInfo.Name] = attrInfo;
            }

            return spec;
        }

        /// <summary>
        /// Load a complete chunk into memory asynchronously.
        /// </summary>
        /// <param name="attributeName">Attribute name (e.g., "TX", "TN")</param>
        /// <param name="chunkIndex">Zero-based chunk index</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>ChunkData containing all values</returns>
        public async Task<ChunkData> LoadChunkAsync(
            string attributeName,
            int chunkIndex,
            CancellationToken cancellationToken = default)
        {
            if (!_specification.Attributes.TryGetValue(attributeName, out var attrInfo))
                throw new ArgumentException($"Attribute not found: {attributeName}", nameof(attributeName));

            if (chunkIndex < 0 || chunkIndex >= attrInfo.Chunks.Count)
                throw new ArgumentOutOfRangeException(nameof(chunkIndex),
                    $"Chunk index must be between 0 and {attrInfo.Chunks.Count - 1}");

            var chunkInfo = attrInfo.Chunks[chunkIndex];
            string filePath = Path.Combine(_dataFolderPath, chunkInfo.File);

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Chunk file not found: {filePath}");

            // Validate file size
            var fileInfo = new FileInfo(filePath);
            long expectedSize = (long)chunkInfo.NumCoordinates * chunkInfo.NumDays * sizeof(float);
            if (fileInfo.Length != expectedSize)
                throw new InvalidDataException(
                    $"File size mismatch: expected {expectedSize} bytes, got {fileInfo.Length} bytes");

            // Read binary data
            int totalValues = chunkInfo.NumCoordinates * chunkInfo.NumDays;
            float[] data = new float[totalValues];

            using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 81920, useAsync: true))
            using (var reader = new BinaryReader(fileStream))
            {
                for (int i = 0; i < totalValues; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    float value = reader.ReadSingle();

                    // Convert NetCDF _FillValue (-99.99) to NaN
                    // E-OBS data uses -99.99 as missing value marker
                    if (Math.Abs(value + 99.99f) < 0.01f)
                    {
                        data[i] = float.NaN;
                    }
                    else
                    {
                        data[i] = value;
                    }
                }
            }

            return new ChunkData
            {
                Info = chunkInfo,
                AttributeName = attributeName,
                Data = data
            };
        }

        /// <summary>
        /// Load a single coordinate's time series from a chunk asynchronously.
        /// More efficient than loading entire chunk if only one coordinate is needed.
        /// </summary>
        /// <param name="attributeName">Attribute name (e.g., "TX", "TN")</param>
        /// <param name="chunkIndex">Zero-based chunk index</param>
        /// <param name="coordIndex">Zero-based coordinate index</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Array of float values for all days at this coordinate</returns>
        public async Task<float[]> GetCoordinateTimeSeriesAsync(
            string attributeName,
            int chunkIndex,
            int coordIndex,
            CancellationToken cancellationToken = default)
        {
            if (!_specification.Attributes.TryGetValue(attributeName, out var attrInfo))
                throw new ArgumentException($"Attribute not found: {attributeName}", nameof(attributeName));

            if (chunkIndex < 0 || chunkIndex >= attrInfo.Chunks.Count)
                throw new ArgumentOutOfRangeException(nameof(chunkIndex));

            var chunkInfo = attrInfo.Chunks[chunkIndex];

            if (coordIndex < 0 || coordIndex >= chunkInfo.NumCoordinates)
                throw new ArgumentOutOfRangeException(nameof(coordIndex));

            string filePath = Path.Combine(_dataFolderPath, chunkInfo.File);

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Chunk file not found: {filePath}");

            float[] timeSeries = new float[chunkInfo.NumDays];

            // Seek to the coordinate's data block
            long offset = (long)coordIndex * chunkInfo.NumDays * sizeof(float);

            using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 4096, useAsync: true))
            {
                fileStream.Seek(offset, SeekOrigin.Begin);

                using (var reader = new BinaryReader(fileStream))
                {
                    for (int i = 0; i < chunkInfo.NumDays; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        float value = reader.ReadSingle();

                        // Convert NetCDF _FillValue (-99.99) to NaN
                        if (Math.Abs(value + 99.99f) < 0.01f)
                        {
                            timeSeries[i] = float.NaN;
                        }
                        else
                        {
                            timeSeries[i] = value;
                        }
                    }
                }
            }

            return timeSeries;
        }

        /// <summary>
        /// Stream all chunks for a given attribute as an observable sequence.
        /// Useful for processing large datasets chunk-by-chunk for GPU streaming.
        /// </summary>
        /// <param name="attributeName">Attribute name (e.g., "TX", "TN")</param>
        /// <returns>Observable sequence of ChunkData objects</returns>
        public IObservable<ChunkData> StreamChunksAsync(string attributeName)
        {
            if (!_specification.Attributes.TryGetValue(attributeName, out var attrInfo))
                throw new ArgumentException($"Attribute not found: {attributeName}", nameof(attributeName));

            return Observable.Create<ChunkData>(async (observer, cancellationToken) =>
            {
                try
                {
                    for (int i = 0; i < attrInfo.Chunks.Count; i++)
                    {
                        var chunkData = await LoadChunkAsync(attributeName, i, cancellationToken);
                        observer.OnNext(chunkData);
                    }
                    observer.OnCompleted();
                }
                catch (Exception ex)
                {
                    observer.OnError(ex);
                }
            });
        }

        /// <summary>
        /// Stream chunks as flattened float arrays (IReadOnlyList for vvvv compatibility).
        /// </summary>
        /// <param name="attributeName">Attribute name (e.g., "TX", "TN")</param>
        /// <returns>Observable sequence of float arrays</returns>
        public IObservable<IReadOnlyList<float>> StreamChunksAsFloatArraysAsync(string attributeName)
        {
            return StreamChunksAsync(attributeName)
                .Select(chunk => (IReadOnlyList<float>)chunk.Data);
        }

        /// <summary>
        /// Get list of all available attribute names.
        /// </summary>
        public IEnumerable<string> GetAttributeNames()
        {
            return _specification.Attributes.Keys;
        }

        /// <summary>
        /// Get information about a specific attribute.
        /// </summary>
        public AttributeInfo GetAttributeInfo(string attributeName)
        {
            if (!_specification.Attributes.TryGetValue(attributeName, out var attrInfo))
                throw new ArgumentException($"Attribute not found: {attributeName}", nameof(attributeName));

            return attrInfo;
        }

        /// <summary>
        /// Get the number of chunks for a given attribute.
        /// </summary>
        public int GetChunkCount(string attributeName)
        {
            if (!_specification.Attributes.TryGetValue(attributeName, out var attrInfo))
                throw new ArgumentException($"Attribute not found: {attributeName}", nameof(attributeName));

            return attrInfo.Chunks.Count;
        }
    }
}
