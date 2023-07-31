using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using OpenTK;
using PSXPrev.Common.Animator;

namespace PSXPrev.Common.Exporters
{
    // todo: Model is upside down as simply changing the vertices Y and Z as we do in the viewer is screwing up the results. Figuring out what is going on here
    public class glTF2Exporter
    {
        private StreamWriter _writer;
        private BinaryWriter _binaryWriter;

        private PNGExporter _pngExporter;
        private ModelPreparerExporter _modelPreparer;
        private string _baseName;
        private string _baseTextureName;
        private ExportModelOptions _options;
        private string _selectedPath;

        private const int ComponentType_Float = 5126;
        private const int MagFilter_Linear = 9728;
        private const int WrapMode_Repeat = 10497;
        private const int PrimitiveMode_Triangles = 4;
        private const int Target_ArrayBuffer = 34962;

        private const string AssetTemplate = " \"asset\" : {\r\n  \"generator\" : \"PSXPREV\",\r\n  \"version\" : \"2.0\"\r\n },";

        public void Export(RootEntity[] entities, Animation[] animations, AnimationBatch animationBatch, string selectedPath, ExportModelOptions options = null)
        {
            _options = options?.Clone() ?? new ExportModelOptions();
            // Force any required options for this format here, before calling Validate.
            _options.Validate();

            _pngExporter = new PNGExporter();
            _modelPreparer = new ModelPreparerExporter(_options);
            _selectedPath = selectedPath;

            // Prepare the shared state for all models being exported (mainly setting up tiled textures).
            _modelPreparer.PrepareAll(entities);

            if (!_options.MergeEntities)
            {
                for (var i = 0; i < entities.Length; i++)
                {
                    ExportEntities(i, animations, animationBatch, entities[i]);
                }
            }

            _pngExporter = null;
            _modelPreparer.Dispose();
            _modelPreparer = null;
        }

        private void ExportEntities(int index, Animation[] animations, AnimationBatch animationBatch, params RootEntity[] entities)
        {
            var exportAnimations = _options.ExportAnimations && animations?.Length > 0;

            _baseName = $"obj{index}";
            _baseTextureName = (_options.ShareTextures ? "objshared" : _baseName) + "_";

            for (var entityIndex = 0; entityIndex < entities.Length; entityIndex++)
            {
                var entity = entities[entityIndex];
                _writer = new StreamWriter($"{_selectedPath}\\{_baseName}_{entityIndex}.gltf");
                _writer.WriteLine("{");

                var models = _modelPreparer.GetModels(entity);

                // Binary buffer creation
                var binaryBufferShortFilename = $"{_baseName}_{entityIndex}.bin";
                var binaryBufferFilename = $"{_selectedPath}\\{binaryBufferShortFilename}";
                _binaryWriter = new BinaryWriter(File.OpenWrite(binaryBufferFilename));

                // Write Asset
                _writer.WriteLine(AssetTemplate);

                // Write Scenes
                {
                    _writer.WriteLine("\"scene\": 0,");
                    _writer.WriteLine(" \"scenes\": [");
                    _writer.WriteLine("  {");
                    _writer.WriteLine("  \"nodes\": [");
                    for (var i = 0; i < models.Count; i++)
                    {
                        _writer.WriteLine(i == models.Count - 1 ? $"{i}" : $"{i},");
                    }
                    _writer.WriteLine("  ]");
                    _writer.WriteLine(" }");
                    _writer.WriteLine("],");
                }

                // Write Buffer Views
                {
                    var initialOffset = _binaryWriter.BaseStream.Position;
                    var offset = initialOffset;
                    _writer.WriteLine("\"bufferViews\": [");
                    {
                        // Meshes
                        for (var i = 0; i < models.Count; i++)
                        {
                            var model = models[i];
                            WriteMeshBufferViews(model, ref offset, initialOffset, !exportAnimations && i == models.Count - 1);
                        }
                    }
                    // Animations
                    if (exportAnimations)
                    {
                        for (var i = 0; i < animations.Length; i++)
                        {
                            var animation = animations[i];
                            var totalTime = animation.FrameCount / animation.FPS;
                            var timeStep = 1f / animation.FPS;
                            WriteAnimationTimeBufferView(totalTime, timeStep, ref offset, initialOffset);
                            for (var j = 0; j < models.Count; j++)
                            {
                                var model = models[j];
                                WriteAnimationDataBufferViews(entities, model, animation, animationBatch, totalTime, timeStep, ref offset, initialOffset, j == models.Count - 1 && i == animations.Length - 1);
                            }
                        }
                    }
                    _writer.WriteLine("],");
                }

                var modelImages = new OrderedDictionary();

                // Write Textures
                if (_options.ExportTextures)
                {
                    // Sampler
                    _writer.WriteLine("\"samplers\": [");
                    _writer.WriteLine(" {");
                    _writer.WriteLine($"  \"magFilter\": {MagFilter_Linear},");
                    _writer.WriteLine($"  \"minFilter\": {MagFilter_Linear},");
                    _writer.WriteLine($"  \"wrapS\": {WrapMode_Repeat},");
                    _writer.WriteLine($"  \"wrapT\": {WrapMode_Repeat}");
                    _writer.WriteLine(" }");
                    _writer.WriteLine("],");

                    // Images
                    var textureImages = new OrderedDictionary();

                    _writer.WriteLine("\"images\": [");
                    for (var i = 0; i < models.Count; i++)
                    {
                        var model = models[i];
                        if (model.Texture != null && model.IsTextured)
                        {
                            int imageId;
                            if (!textureImages.Contains(model.Texture))
                            {
                                imageId = textureImages.Count;
                                textureImages.Add(model.Texture, imageId);
                                var uri = $"{_baseTextureName}{imageId}";
                                _writer.WriteLine(modelImages.Count > 0 ? ", {" : "{");
                                _pngExporter.Export(model.Texture, uri, _selectedPath);
                                _writer.WriteLine($" \"uri\": \"{uri}.png\"");
                                _writer.WriteLine("}");
                            }
                            else
                            {
                                imageId = (int)textureImages[model.Texture];
                            }
                            modelImages.Add(model, imageId);
                        }
                    }
                    _writer.WriteLine("],");

                    // Textures
                    _writer.WriteLine("\"textures\": [");
                    for (var i = 0; i < textureImages.Count; i++)
                    {
                        var imageId = (int)textureImages[i];
                        _writer.WriteLine("{");
                        _writer.WriteLine($" \"source\": {imageId},");
                        _writer.WriteLine(" \"sampler\": 0");
                        _writer.WriteLine(i == textureImages.Count - 1 ? "}" : "}, ");
                    }
                    _writer.WriteLine("],");
                }

                // Write Materials
                {
                    _writer.WriteLine("\"materials\": [");
                    for (var i = 0; i < models.Count; i++)
                    {
                        var model = models[i];
                        _writer.WriteLine("{");
                        _writer.WriteLine("\"pbrMetallicRoughness\" : {");
                        if (modelImages.Contains(model))
                        {
                            var imageId = (int)modelImages[model];
                            _writer.WriteLine("\"baseColorTexture\" : {");
                            _writer.WriteLine($"\"index\" : {imageId}");
                            _writer.WriteLine("},");
                        }
                        _writer.WriteLine("\"metallicFactor\" : 0.0,");
                        _writer.WriteLine("\"roughnessFactor\" : 1.0");
                        _writer.WriteLine("}");
                        _writer.WriteLine(i == models.Count - 1 ? "}" : "}, ");
                    }
                    _writer.WriteLine("],");
                }

                // Write Accessors
                {
                    _writer.WriteLine("\"accessors\": [");

                    var bufferViewIndex = 0;
                    // Meshes
                    {
                        for (var i = 0; i < models.Count; i++)
                        {
                            var model = models[i];
                            var vertexCount = model.Triangles.Length * 3;
                            var boundsMin = new[] { model.Bounds3D.Min.X, model.Bounds3D.Min.Y, model.Bounds3D.Min.Z };
                            var boundsMax = new[] { model.Bounds3D.Max.X, model.Bounds3D.Max.Y, model.Bounds3D.Max.Z };
                            //todo: are bounds inverted bc of y-z negation?
                            WriteAccessor(bufferViewIndex++, 0, ComponentType_Float, vertexCount, "VEC3", false, boundsMin, boundsMax); // Vertex positions
                            WriteAccessor(bufferViewIndex++, 0, ComponentType_Float, vertexCount, "VEC3"); // Vertex colors
                            WriteAccessor(bufferViewIndex++, 0, ComponentType_Float, vertexCount, "VEC3"); // Vertex normals
                            WriteAccessor(bufferViewIndex++, 0, ComponentType_Float, vertexCount, "VEC2", !exportAnimations && i == models.Count - 1); // Vertex uvs
                        }
                    }
                    // Animations
                    if (exportAnimations)
                    {
                        for (var i = 0; i < animations.Length; i++)
                        {
                            var animation = animations[i];
                            var totalTime = animation.FrameCount / animation.FPS;
                            var timeStep = 1f / animation.FPS;
                            var stepCount = (int)Math.Ceiling(totalTime / timeStep);
                            var timeMin = new float[] { 0f };
                            var timeMax = new float[] { totalTime };
                            WriteAccessor(bufferViewIndex++, 0, ComponentType_Float, stepCount, "SCALAR", false, timeMin, timeMax); // Frame times
                            for (var j = 0; j < models.Count; j++)
                            {
                                WriteAccessor(bufferViewIndex++, 0, ComponentType_Float, stepCount, "VEC3"); // Object translation
                                WriteAccessor(bufferViewIndex++, 0, ComponentType_Float, stepCount, "VEC4"); // Object rotation
                                WriteAccessor(bufferViewIndex++, 0, ComponentType_Float, stepCount, "VEC3", i == animations.Length - 1 && j == models.Count - 1); // Object scale
                            }
                        }
                    }
                    _writer.WriteLine("],");
                }

                var accessorIndex = 0;

                // Write Meshes
                {
                    _writer.WriteLine("\"meshes\": [");
                    for (var i = 0; i < models.Count; i++)
                    {
                        var model = models[i];
                        _writer.WriteLine("{");
                        _writer.WriteLine($" \"name\": \"{model.EntityName}\",");
                        _writer.WriteLine(" \"primitives\": [");
                        _writer.WriteLine("  {");
                        _writer.WriteLine($"   \"attributes\": {{");
                        _writer.WriteLine($"    \"POSITION\": {accessorIndex++},");
                        _writer.WriteLine($"    \"COLOR_0\": {accessorIndex++},");
                        _writer.WriteLine($"    \"NORMAL\": {accessorIndex++},");
                        _writer.WriteLine($"    \"TEXCOORD_0\": {accessorIndex++}");
                        _writer.WriteLine("   },");
                        _writer.WriteLine($"   \"material\": {i},");
                        _writer.WriteLine($"   \"mode\": {PrimitiveMode_Triangles}");
                        _writer.WriteLine("  }");
                        _writer.WriteLine(" ]");
                        _writer.WriteLine(i == models.Count - 1 ? "}" : "},");
                    }
                    _writer.WriteLine("],");
                }

                // Write Animations 
                if (exportAnimations)
                {
                    _writer.WriteLine("\"animations\": [");
                    for (var i = 0; i < animations.Length; i++)
                    {
                        var animationSamplerIndex = 0;
                        var timeAccessorIndex = accessorIndex++;

                        _writer.WriteLine("{");
                        // Samplers
                        _writer.WriteLine(" \"samplers\" : [");
                        for (var j = 0; j < models.Count; j++)
                        {
                            WriteAnimationSampler(timeAccessorIndex, "LINEAR", accessorIndex++); // object translation
                            WriteAnimationSampler(timeAccessorIndex, "LINEAR", accessorIndex++); // object rotation
                            WriteAnimationSampler(timeAccessorIndex, "LINEAR", accessorIndex++, j == models.Count - 1); // object scale
                        }
                        _writer.WriteLine("],");

                        // Channels
                        _writer.WriteLine(" \"channels\" : [");
                        for (var j = 0; j < models.Count; j++)
                        {
                            WriteAnimationChannel(animationSamplerIndex++, "translation", j); // object translation
                            WriteAnimationChannel(animationSamplerIndex++, "rotation", j); // object rotation
                            WriteAnimationChannel(animationSamplerIndex++, "scale", j, j == models.Count - 1); // object scale
                        }
                        _writer.WriteLine("  ]");
                        _writer.WriteLine(i == animations.Length - 1 ? "}" : "}, ");
                    }
                    _writer.WriteLine("],");
                }

                // Write Nodes
                _writer.WriteLine("\"nodes\": [");
                for (var i = 0; i < models.Count; i++)
                {
                    var model = models[i];
                    _writer.WriteLine("{");
                    _writer.WriteLine($" \"mesh\": {i},");
                    _writer.WriteLine($" \"name\": \"{model.EntityName}\",");
                    _writer.WriteLine(" \"translation\": [");
                    WriteVector3(model.Translation, false);
                    _writer.WriteLine(" ],");
                    _writer.WriteLine(" \"scale\": [");
                    WriteVector3(model.Scale, false);
                    _writer.WriteLine(" ]");
                    _writer.WriteLine(i == models.Count - 1 ? "}" : "},");
                }
                _writer.WriteLine("],");

                //Write Buffers
                _writer.WriteLine("\"buffers\": [");
                _writer.WriteLine(" {");
                _writer.WriteLine($"  \"uri\": \"{binaryBufferShortFilename}\",");
                _writer.WriteLine($"  \"byteLength\": {_binaryWriter.BaseStream.Length}");
                _writer.WriteLine(" }");
                _writer.WriteLine("]");

                _writer.WriteLine("}");

                _binaryWriter.Dispose();
                _binaryWriter = null;

                _writer.Dispose();
                _writer = null;
            }
        }
        private void WriteAnimationChannel(int sampler, string path, int node, bool final = false)
        {
            _writer.WriteLine("{");
            _writer.WriteLine($" \"sampler\": {sampler},");
            _writer.WriteLine(" \"target\": {");
            _writer.WriteLine($" \"node\": {node},");
            _writer.WriteLine($" \"path\": \"{path}\"");
            _writer.WriteLine(" }");
            _writer.WriteLine(final ? "}" : "}, ");
        }

        private void WriteAnimationSampler(int input, string interpolation, int output, bool final = false)
        {
            _writer.WriteLine("{");
            _writer.WriteLine($" \"input\": {input},");
            _writer.WriteLine($" \"interpolation\": \"{interpolation}\",");
            _writer.WriteLine($" \"output\": {output}");
            _writer.WriteLine(final ? "}" : "}, ");
        }

        private void WriteAccessor(int bufferView, int byteOffset, int componentType, int count, string type, bool final = false, float[] min = null, float[] max = null)
        {
            _writer.WriteLine("{");
            _writer.WriteLine($" \"bufferView\": {bufferView},");
            _writer.WriteLine($" \"byteOffset\": {byteOffset},");
            _writer.WriteLine($" \"componentType\": {componentType},");
            _writer.WriteLine($" \"count\": {count},");
            _writer.WriteLine($" \"type\": \"{type}\"");
            if (min != null && max != null)
            {
                _writer.WriteLine(",");
                _writer.WriteLine(" \"min\": [");
                for (var i = 0; i < min.Length; i++)
                {
                    _writer.WriteLine($"  {F(min[i])}");
                    if (i < min.Length - 1)
                    {
                        _writer.Write(",");
                    }
                }
                _writer.WriteLine(" ],");
                _writer.WriteLine(" \"max\": [");
                for (var i = 0; i < max.Length; i++)
                {
                    _writer.WriteLine($"  {F(max[i])}");
                    if (i < max.Length - 1)
                    {
                        _writer.Write(",");
                    }
                }
                _writer.WriteLine(" ]");
            }
            _writer.WriteLine(final ? "}" : "},");
        }

        private void WriteAnimationTimeBufferView(float totalTime, float timeStep, ref long offset, long initialOffset)
        {
            // Write time
            _writer.WriteLine("{");
            _writer.WriteLine(" \"buffer\": 0,");
            _writer.WriteLine($" \"byteOffset\": {_binaryWriter.BaseStream.Position - initialOffset},");
            for (var time = 0f; time < totalTime; time += timeStep)
            {
                WriteBinaryFloat(time);
            }
            _writer.WriteLine($" \"byteLength\": {_binaryWriter.BaseStream.Position - offset}");
            _writer.WriteLine("},");

            offset = _binaryWriter.BaseStream.Position;
        }

        // todo: we could avoid so many loops by intercalating data
        private void WriteAnimationDataBufferViews(RootEntity[] entities, ModelEntity model, Animation animation, AnimationBatch animationBatch, float totalTime, float timeStep, ref long offset, long initialOffset, bool final)
        {
            // Write position
            _writer.WriteLine("{");
            _writer.WriteLine(" \"buffer\": 0,");
            _writer.WriteLine($" \"byteOffset\": {_binaryWriter.BaseStream.Position - initialOffset},");
            animationBatch.SetupAnimationBatch(animation);
            animationBatch.LoopMode = AnimationLoopMode.Once;
            for (var t = 0f; t < totalTime; t += timeStep)
            {
                animationBatch.Time = t;
                if (animationBatch.SetupAnimationFrame(entities, null, model))
                {
                    var matrix = model.TempMatrix;
                    var translation = matrix.ExtractTranslation();
                    WriteBinaryVector3(translation, false);
                }
            }
            _writer.WriteLine($" \"byteLength\": {_binaryWriter.BaseStream.Position - offset}");
            _writer.WriteLine("},");

            offset = _binaryWriter.BaseStream.Position;

            // Write rotation
            _writer.WriteLine("{");
            _writer.WriteLine(" \"buffer\": 0,");
            _writer.WriteLine($" \"byteOffset\": {_binaryWriter.BaseStream.Position - initialOffset},");
            animationBatch.SetupAnimationBatch(animation);
            animationBatch.LoopMode = AnimationLoopMode.Once;
            for (var t = 0f; t < totalTime; t += timeStep)
            {
                animationBatch.Time = t;
                if (animationBatch.SetupAnimationFrame(entities, null, model))
                {
                    var matrix = model.TempMatrix;
                    var rotation = matrix.ExtractRotation();
                    WriteBinaryQuaternion(rotation);
                }
            }
            _writer.WriteLine($" \"byteLength\": {_binaryWriter.BaseStream.Position - offset}");
            _writer.WriteLine("},");

            offset = _binaryWriter.BaseStream.Position;

            // Write scale
            _writer.WriteLine("{");
            _writer.WriteLine(" \"buffer\": 0,");
            _writer.WriteLine($" \"byteOffset\": {_binaryWriter.BaseStream.Position - initialOffset},");
            animationBatch.SetupAnimationBatch(animation);
            animationBatch.LoopMode = AnimationLoopMode.Once;
            for (var t = 0f; t < totalTime; t += timeStep)
            {
                animationBatch.Time = t;
                if (animationBatch.SetupAnimationFrame(entities, null, model))
                {
                    var matrix = model.TempMatrix;
                    var scale = matrix.ExtractScale();
                    WriteBinaryVector3(scale, false);
                }
            }
            _writer.WriteLine($" \"byteLength\": {_binaryWriter.BaseStream.Position - offset}");
            _writer.WriteLine(final ? "}" : "},");

            offset = _binaryWriter.BaseStream.Position;
        }

        private void WriteMeshBufferViews(ModelEntity model, ref long offset, long initialOffset, bool final = false)
        {
            // Write vertex positions
            _writer.WriteLine("{");
            _writer.WriteLine(" \"buffer\": 0,");
            _writer.WriteLine($" \"target\": {Target_ArrayBuffer},");
            _writer.WriteLine($" \"byteOffset\": {_binaryWriter.BaseStream.Position - initialOffset},");
            foreach (var triangle in model.Triangles)
            {
                for (var j = 2; j >= 0; j--)
                {
                    WriteBinaryVector3(triangle.Vertices[j], false);
                }
            }
            _writer.WriteLine($" \"byteLength\": {_binaryWriter.BaseStream.Position - offset}");
            _writer.WriteLine("},");

            offset = _binaryWriter.BaseStream.Position;

            // Write vertex colors
            _writer.WriteLine("{");
            _writer.WriteLine(" \"buffer\": 0,");
            _writer.WriteLine($" \"target\": {Target_ArrayBuffer},");
            _writer.WriteLine($" \"byteOffset\": {_binaryWriter.BaseStream.Position - initialOffset},");
            foreach (var triangle in model.Triangles)
            {
                for (var j = 2; j >= 0; j--)
                {
                    WriteBinaryColor(triangle.Colors[j]);
                }
            }
            _writer.WriteLine($" \"byteLength\": {_binaryWriter.BaseStream.Position - offset}");
            _writer.WriteLine("},");

            offset = _binaryWriter.BaseStream.Position;

            // Write vertex normals
            _writer.WriteLine("{");
            _writer.WriteLine(" \"buffer\": 0,");
            _writer.WriteLine($" \"target\": {Target_ArrayBuffer},");
            _writer.WriteLine($" \"byteOffset\": {_binaryWriter.BaseStream.Position - initialOffset},");
            foreach (var triangle in model.Triangles)
            {
                for (var j = 2; j >= 0; j--)
                {
                    WriteBinaryVector3(triangle.Normals[j], false);
                }
            }
            _writer.WriteLine($" \"byteLength\": {_binaryWriter.BaseStream.Position - offset}");
            _writer.WriteLine("},");

            offset = _binaryWriter.BaseStream.Position;

            // Write vertex UVs
            _writer.WriteLine("{");
            _writer.WriteLine(" \"buffer\": 0,");
            _writer.WriteLine($" \"target\": {Target_ArrayBuffer},");
            _writer.WriteLine($" \"byteOffset\": {_binaryWriter.BaseStream.Position - initialOffset},");
            foreach (var triangle in model.Triangles)
            {
                for (var j = 2; j >= 0; j--)
                {
                    WriteBinaryUV(triangle.Uv[j]);
                }
            }
            _writer.WriteLine($" \"byteLength\": {_binaryWriter.BaseStream.Position - offset}");
            _writer.WriteLine(final ? "}" : "},");

            offset = _binaryWriter.BaseStream.Position;
        }

        private void WriteVector3(Vector3 vector, bool fixHandiness = false)
        {
            _writer.WriteLine($"  {F(vector.X)},");
            if (fixHandiness)
            {
                _writer.WriteLine($"  {F(-vector.Y)},");
                _writer.WriteLine($"  {F(-vector.Z)}");
            }
            else
            {
                _writer.WriteLine($"  {F(vector.Y)},");
                _writer.WriteLine($"  {F(vector.Z)}");
            }
        }

        private void WriteBinaryColor(Color color)
        {
            _binaryWriter.Write(color.R);
            _binaryWriter.Write(color.G);
            _binaryWriter.Write(color.B);
        }

        private void WriteBinaryFloat(float value)
        {
            _binaryWriter.Write(value);
        }

        private void WriteBinaryVector3(Vector3 vector, bool fixHandiness = false)
        {
            _binaryWriter.Write(vector.X);
            if (fixHandiness)
            {
                _binaryWriter.Write(-vector.Y);
                _binaryWriter.Write(-vector.Z);
            }
            else
            {
                _binaryWriter.Write(vector.Y);
                _binaryWriter.Write(vector.Z);
            }
        }

        private void WriteBinaryQuaternion(Quaternion quaternion)
        {
            _binaryWriter.Write(quaternion.X);
            _binaryWriter.Write(quaternion.Y);
            _binaryWriter.Write(quaternion.Z);
            _binaryWriter.Write(quaternion.W);
        }

        private void WriteBinaryUV(Vector2 vector)
        {
            _binaryWriter.Write(vector.X);
            _binaryWriter.Write(vector.Y);
        }

        private static string F(float value)
        {
            return value.ToString(GeomMath.FloatFormat, CultureInfo.InvariantCulture);
        }
    }
}