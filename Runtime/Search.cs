using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Sentis;
using Cloud.Unum.USearch;


namespace LLMUnity
{
    public abstract class ModelSearchBase
    {
        protected EmbeddingModel embedder;
        protected Dictionary<string, TensorFloat> embeddings;

        public ModelSearchBase(EmbeddingModel embedder)
        {
            embeddings = new Dictionary<string, TensorFloat>();
            this.embedder = embedder;
        }

        public TensorFloat Encode(string inputString)
        {
            return embedder.Encode(inputString);
        }

        public TensorFloat[] Encode(List<string> inputStrings, int batchSize = 64)
        {
            List<TensorFloat> inputEmbeddings = new List<TensorFloat>();
            for (int i = 0; i < inputStrings.Count; i += batchSize)
            {
                int takeCount = Math.Min(batchSize, inputStrings.Count - i);
                List<string> batch = new List<string>(inputStrings.GetRange(i, takeCount));
                inputEmbeddings.AddRange((TensorFloat[])embedder.Split(embedder.Encode(batch)));
            }
            if (inputStrings.Count != inputEmbeddings.Count)
            {
                throw new Exception($"Number of computed embeddings ({inputEmbeddings.Count}) different than inputs ({inputStrings.Count})");
            }
            return inputEmbeddings.ToArray();
        }

        public virtual string[] Search(TensorFloat encoding, int k)
        {
            return Search(encoding, k, out float[] distances);
        }

        public virtual string[] Search(string queryString, int k)
        {
            return Search(queryString, k, out float[] distances);
        }

        public virtual string[] Search(TensorFloat encoding, int k, out float[] distances)
        {
            TensorFloat storeEmbedding = embedder.Concat(embeddings.Values.ToArray());
            float[] unsortedDistances = embedder.SimilarityDistances(encoding, storeEmbedding);

            var sortedLists = embeddings.Keys.Zip(unsortedDistances, (first, second) => new { First = first, Second = second })
                .OrderBy(item => item.Second)
                .ToList();
            int kmax = k == -1 ? sortedLists.Count : Math.Min(k, sortedLists.Count);
            string[] results = new string[kmax];
            distances = new float[kmax];
            for (int i = 0; i < kmax; i++)
            {
                results[i] = sortedLists[i].First;
                distances[i] = sortedLists[i].Second;
            }
            return results;
        }

        public virtual string[] Search(string queryString, int k, out float[] distances)
        {
            return Search(embedder.Encode(queryString), k, out distances);
        }

        public virtual int Count()
        {
            return embeddings.Count;
        }
    }

    public class ModelSearch : ModelSearchBase
    {
        public ModelSearch(EmbeddingModel embedder) : base(embedder) {}

        protected void Insert(string inputString, TensorFloat encoding)
        {
            embeddings[inputString] = encoding;
        }

        public TensorFloat Add(string inputString)
        {
            TensorFloat embedding = Encode(inputString);
            Insert(inputString, embedding);
            return embedding;
        }

        public TensorFloat[] Add(string[] inputStrings, int batchSize = 64)
        {
            return Add(new List<string>(inputStrings), batchSize);
        }

        public TensorFloat[] Add(List<string> inputStrings, int batchSize = 64)
        {
            TensorFloat[] inputEmbeddings = Encode(inputStrings, batchSize);
            for (int i = 0; i < inputStrings.Count; i++)
            {
                Insert(inputStrings[i], inputEmbeddings[i]);
            }
            return inputEmbeddings.ToArray();
        }
    }

    public class ModelKeySearch : ModelSearchBase
    {
        protected Dictionary<string, int> valueToKey;

        public ModelKeySearch(EmbeddingModel embedder) : base(embedder)
        {
            valueToKey = new Dictionary<string, int>();
        }

        public virtual void Insert(int key, string value, TensorFloat encoding)
        {
            embeddings[value] = encoding;
            valueToKey[value] = key;
        }

        public virtual TensorFloat Add(int key, string inputString)
        {
            TensorFloat embedding = Encode(inputString);
            Insert(key, inputString, embedding);
            return embedding;
        }

        public virtual TensorFloat[] Add(int[] keys, string[] inputStrings, int batchSize = 64)
        {
            return Add(new List<int>(keys), new List<string>(inputStrings), batchSize);
        }

        public virtual TensorFloat[] Add(List<int> keys, List<string> inputStrings, int batchSize = 64)
        {
            TensorFloat[] inputEmbeddings = Encode(inputStrings, batchSize);
            for (int i = 0; i < inputStrings.Count; i++)
            {
                Insert(keys[i], inputStrings[i], inputEmbeddings[i]);
            }
            return inputEmbeddings.ToArray();
        }

        public virtual int[] SearchKey(TensorFloat embedding, int k, out float[] distances)
        {
            string[] results = Search(embedding, k, out distances);
            int[] keys = new int[results.Length];
            for (int i = 0; i < results.Length; i++)
            {
                keys[i] = valueToKey[results[i]];
            }
            return keys;
        }

        public virtual int[] SearchKey(string queryString, int k, out float[] distances)
        {
            return SearchKey(Encode(queryString), k, out distances);
        }

        public virtual int[] SearchKey(TensorFloat encoding, int k)
        {
            return SearchKey(encoding, k, out float[] distances);
        }

        public virtual int[] SearchKey(string queryString, int k)
        {
            return SearchKey(queryString, k, out float[] distances);
        }
    }

    public class ANNModelSearch : ModelKeySearch
    {
        USearchIndex index;
        protected Dictionary<int, string> keyToValue;

        public ANNModelSearch(EmbeddingModel embedder) : this(embedder, MetricKind.Cos, 32, 40, 16) {}

        public ANNModelSearch(
            EmbeddingModel embedder,
            MetricKind metricKind = MetricKind.Cos,
            ulong connectivity = 32,
            ulong expansionAdd = 40,
            ulong expansionSearch = 16,
            bool multi = false
        ) : this(embedder, new USearchIndex((ulong)embedder.Dimensions, metricKind, connectivity, expansionAdd, expansionSearch, multi)) {}

        public ANNModelSearch(
            EmbeddingModel embedder,
            USearchIndex index
        ) : base(embedder)
        {
            this.index = index;
            keyToValue = new Dictionary<int, string>();
        }

        public override void Insert(int key, string value, TensorFloat encoding)
        {
            encoding.MakeReadable();
            index.Add((ulong)key, encoding.ToReadOnlyArray());
            keyToValue[key] = value;
        }

        public override string[] Search(TensorFloat encoding, int k)
        {
            return Search(encoding, k, out float[] distances);
        }

        public override string[] Search(string queryString, int k)
        {
            return Search(queryString, k, out float[] distances);
        }

        public override string[] Search(TensorFloat encoding, int k, out float[] distances)
        {
            int[] results = SearchKey(encoding, k, out distances);
            string[] values = new string[results.Length];
            for (int i = 0; i < results.Length; i++)
            {
                values[i] = keyToValue[results[i]];
            }
            return values;
        }

        public override string[] Search(string queryString, int k, out float[] distances)
        {
            return Search(embedder.Encode(queryString), k, out distances);
        }

        public override int[] SearchKey(TensorFloat encoding, int k)
        {
            return SearchKey(encoding, k, out float[] distances);
        }

        public override int[] SearchKey(string queryString, int k)
        {
            return SearchKey(queryString, k, out float[] distances);
        }

        public override int[] SearchKey(TensorFloat encoding, int k, out float[] distances)
        {
            encoding.MakeReadable();
            index.Search(encoding.ToReadOnlyArray(), k, out ulong[] keys, out distances);

            int[] intKeys = new int[keys.Length];
            for (int i = 0; i < keys.Length; i++)
                intKeys[i] = (int)keys[i];
            return intKeys;
        }

        public override int[] SearchKey(string queryString, int k, out float[] distances)
        {
            return SearchKey(embedder.Encode(queryString), k, out distances);
        }

        public override int Count()
        {
            return (int)index.Size();
        }
    }
}