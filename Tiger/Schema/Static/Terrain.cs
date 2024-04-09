using System.Diagnostics;
using Arithmic;
using Tiger.Exporters;
using Tiger.Schema.Model;
using Tiger.Schema.Shaders;

namespace Tiger.Schema.Static;


public class Terrain : Tag<STerrain>
{
    public Terrain(FileHash hash) : base(hash)
    {

    }

    // To test use edz.strike_hmyn and alleys_a adf6ae80
    public void LoadIntoExporter(ExporterScene scene, string saveDirectory, bool bSaveShaders, bool exportStatic = false)
    {
        // Uses triangle strip + only using first set of vertices and indices
        Dictionary<StaticPart, IMaterial> parts = new Dictionary<StaticPart, IMaterial>();
        List<Texture> dyeMaps = new List<Texture>();
        foreach (var partEntry in _tag.StaticParts)
        {
            if (partEntry.DetailLevel == 0)
            {
                if ((partEntry.Material is null || partEntry.Material.VertexShader is null) && Strategy.CurrentStrategy != TigerStrategy.DESTINY1_RISE_OF_IRON)
                    continue;

                var part = MakePart(partEntry);
                parts.TryAdd(part, partEntry.Material);

                scene.Materials.Add(new ExportMaterial(partEntry.Material, true));
                part.Material = partEntry.Material;

                if (exportStatic) //Need access to material early, before scene system exports
                    partEntry.Material.SavePixelShader($"{saveDirectory}/Shaders", true);
            }
        }

        int terrainTextureIndex = 14;
        Texture lastValidEntry = null;
        for (int i = 0; i < _tag.MeshGroups.Count; i++)
        {
            var partEntry = _tag.MeshGroups[i];
            if (partEntry.Dyemap == null)
            {
                if (lastValidEntry != null)
                {
                    // Use the last valid Dyemap for any invalid
                    scene.Textures.Add(lastValidEntry);
                    dyeMaps.Add(lastValidEntry);
                }
                else // Use the first valid dyemap if it gets to this point
                {
                    var firstValidDyemap = _tag.MeshGroups.FirstOrDefault(x => x.Dyemap != null).Dyemap;
                    if (firstValidDyemap != null)
                    {
                        scene.Textures.Add(firstValidDyemap);
                        dyeMaps.Add(firstValidDyemap);
                    }
                }
            }
            else
            {
                // Update lastValidEntry with the current Dyemap
                lastValidEntry = partEntry.Dyemap;
                scene.Textures.Add(partEntry.Dyemap);
                dyeMaps.Add(partEntry.Dyemap);
            }
        }

        foreach (var part in parts)
        {
            TransformPositions(part.Key);
            TransformTexcoords(part.Key);
            TransformVertexColors(part.Key);
        }

        scene.AddStatic(Hash, parts.Keys.ToList());
        // For now we pre-transform it
        if (!exportStatic)
        {
            scene.AddStaticInstance(Hash, 1, Vector4.Zero, Vector3.Zero);

            for (int i = 0; i < dyeMaps.Count; i++)
            {
                scene.AddTerrainDyemap(Hash, dyeMaps[i].Hash);
            }
        }

        if (CharmInstance.GetSubsystem<ConfigSubsystem>().GetS2VMDLExportEnabled())
            Source2Handler.SaveTerrainVMDL(saveDirectory, Hash, parts.Keys.ToList(), TagData);
    }

    public StaticPart MakePart(SStaticPart entry)
    {
        StaticPart part = new(entry);
        part.GroupIndex = entry.GroupIndex;
        part.Indices = _tag.Indices1.GetIndexData(PrimitiveType.TriangleStrip, entry.IndexOffset, entry.IndexCount);
        // Get unique vertex indices we need to get data for
        HashSet<uint> uniqueVertexIndices = new HashSet<uint>();
        foreach (UIntVector3 index in part.Indices)
        {
            uniqueVertexIndices.Add(index.X);
            uniqueVertexIndices.Add(index.Y);
            uniqueVertexIndices.Add(index.Z);
        }
        part.VertexIndices = uniqueVertexIndices.ToList();

        if (Strategy.CurrentStrategy != TigerStrategy.DESTINY1_RISE_OF_IRON)
        {
            List<InputSignature> inputSignatures = entry.Material.VertexShader.InputSignatures;
            int b0Stride = _tag.Vertices1.TagData.Stride;
            int b1Stride = _tag.Vertices2?.TagData.Stride ?? 0;
            List<InputSignature> inputSignatures0 = new();
            List<InputSignature> inputSignatures1 = new();
            int stride = 0;
            foreach (InputSignature inputSignature in inputSignatures)
            {
                if (stride < b0Stride)
                    inputSignatures0.Add(inputSignature);
                else
                    inputSignatures1.Add(inputSignature);

                if (inputSignature.Semantic == InputSemantic.Colour)
                    stride += inputSignature.GetNumberOfComponents() * 1;  // 1 byte per component
                else
                    stride += inputSignature.GetNumberOfComponents() * 2;  // 2 bytes per component
            }

            Log.Debug($"Reading vertex buffers {_tag.Vertices1.Hash}/{_tag.Vertices1.TagData.Stride}/{inputSignatures.Where(s => s.BufferIndex == 0).DebugString()} and {_tag.Vertices2?.Hash}/{_tag.Vertices2?.TagData.Stride}/{inputSignatures.Where(s => s.BufferIndex == 1).DebugString()}");
            _tag.Vertices1.ReadVertexDataSignatures(part, uniqueVertexIndices, inputSignatures0, true);
            _tag.Vertices2.ReadVertexDataSignatures(part, uniqueVertexIndices, inputSignatures1, true);

        }
        else // Can't get input semantics (yet) for D1 / PS4
        {
            _tag.Vertices1.ReadVertexData(part, uniqueVertexIndices, 0, _tag.Vertices2 != null ? _tag.Vertices2.TagData.Stride : -1, true);
            _tag.Vertices2?.ReadVertexData(part, uniqueVertexIndices, 1, _tag.Vertices1.TagData.Stride, true);
        }

        return part;
    }

    public void TransformPositions(StaticPart part)
    {
        Debug.Assert(part.VertexPositions.Count == part.VertexNormals.Count);
        for (int i = 0; i < part.VertexPositions.Count; i++)
        {
            //The "standard" terrain vertex shader from hlsl
            System.Numerics.Vector4 r0, r1, r2 = new();
            System.Numerics.Vector4 v0 = new(part.VertexPositions[i].X, part.VertexPositions[i].Y, part.VertexPositions[i].Z, part.VertexPositions[i].W);
            System.Numerics.Vector4 v1 = new(part.VertexNormals[i].X, part.VertexNormals[i].Y, part.VertexNormals[i].Z, part.VertexNormals[i].W);

            //r0 = cb11.transform + v0;
            r0.X = _tag.Unk30.X + v0.X;
            r0.Y = _tag.Unk30.Y + v0.Y;
            r0.Z = _tag.Unk30.Z + v0.Z;
            r0.W = _tag.Unk30.W + v0.W;

            r0.Z = r0.W * 65536 + r0.Z;

            //r0.xyw = float3(0.015625, 0.015625, 0.000122070313) * r0.xyz;
            r0.X = 0.015625f * r0.X;
            r0.Y = 0.015625f * r0.Y;
            r0.W = 0.000122070313f * r0.Z;

            //r1.xyz = float3(0,1,0) * v1.yzx;
            r1.X = 0 * v1.Y;
            r1.Y = 1 * v1.Z;
            r1.Z = 0 * v1.X;

            //r1.xyz = v1.zxy * float3(0,0,1) + -r1.xyz;
            r1.X = v1.Z * 0 + -r1.X;
            r1.Y = v1.X * 0 + -r1.Y;
            r1.Z = v1.Y * 1 + -r1.Z;

            //r0.z = dot(r1.yz, r1.yz);
            r0.Z = System.Numerics.Vector2.Dot(new(r1.Y, r1.Z), new(r1.Y, r1.Z));

            //r0.z = rsqrt(r0.z);
            r0.Z = MathF.ReciprocalSqrtEstimate(r0.Z);

            //r1.xyz = r1.xyz * r0.zzz;
            r1.X = r1.X * r0.Z;
            r1.Y = r1.Y * r0.Z;
            r1.Z = r1.Z * r0.Z;

            //r2.xyz = v1.zxy * r1.yzx;
            r2.X = v1.Z * r1.Y;
            r2.Y = v1.X * r1.Z;
            r2.Z = v1.Y * r1.X;

            //r2.xyz = v1.yzx * r1.zxy + -r2.xyz;
            r2.X = v1.Y * r1.Z + -r2.X;
            r2.Y = v1.Z * r1.X + -r2.Y;
            r2.Z = v1.X * r1.Y + -r2.Z;

            //r0.z = dot(r2.xyz, r2.xyz);
            //r0.z = rsqrt(r0.z);
            r0.Z = System.Numerics.Vector3.Dot(new(r2.X, r2.Y, r2.Z), new(r2.X, r2.Y, r2.Z));
            r0.Z = MathF.ReciprocalSqrtEstimate(r0.Z);

            part.VertexPositions[i] = new Vector4(r0.X, r0.Y, r0.Z * r0.W, r0.W);
        }
    }

    public void TransformTexcoords(StaticPart part)
    {
        for (int i = 0; i < part.VertexTexcoords0.Count; i++)
        {
            part.VertexTexcoords0[i] = new Vector2(
            part.VertexTexcoords0[i].X * _tag.MeshGroups[part.GroupIndex].Unk20.X + _tag.MeshGroups[part.GroupIndex].Unk20.Z,
            part.VertexTexcoords0[i].Y * -_tag.MeshGroups[part.GroupIndex].Unk20.Y + 1 - _tag.MeshGroups[part.GroupIndex].Unk20.W);
        }
    }

    public void TransformVertexColors(StaticPart part)
    {
        //Helper for dyemap assignment
        //ROI and Pre-BL can have a max of 16 per terrain part
        float alpha = part.GroupIndex / 15.0f;
        for (int i = 0; i < part.VertexPositions.Count; i++)
        {
            part.VertexColours.Add(new Vector4(0.0f, 0.0f, 0.0f, alpha));
        }
    }
}

/// <summary>
/// Terrain data resource.
/// </summary>
[SchemaStruct(TigerStrategy.DESTINY1_RISE_OF_IRON, "371C8080", 0x20)]
[SchemaStruct(TigerStrategy.DESTINY2_SHADOWKEEP_2601, "4B718080", 0x20)]
[SchemaStruct(TigerStrategy.DESTINY2_BEYONDLIGHT_3402, "7D6C8080", 0x20)]
public struct SMapTerrainResource
{
    [SchemaField(0x10)]
    public short Unk10;  // tile x-y coords?
    public short Unk12;
    public TigerHash Unk14;
    [NoLoad]
    public Terrain Terrain;
    public Tag<SOcclusionBounds> TerrainBounds;
}

/// <summary>
/// Terrain _tag.
/// </summary>
[SchemaStruct(TigerStrategy.DESTINY1_RISE_OF_IRON, "2E1B8080", 0xB0)]
[SchemaStruct(TigerStrategy.DESTINY2_SHADOWKEEP_2601, "4F718080", 0xB0)]
[SchemaStruct(TigerStrategy.DESTINY2_BEYONDLIGHT_3402, "816C8080", 0xB0)]
public struct STerrain
{
    public long FileSize;
    [SchemaField(0x10)]
    public Vector4 Unk10;
    public Vector4 Unk20;
    public Vector4 Unk30;
    [SchemaField(0x58, TigerStrategy.DESTINY1_RISE_OF_IRON)]
    [SchemaField(0x50, TigerStrategy.DESTINY2_BEYONDLIGHT_3402)]
    public DynamicArray<SMeshGroup> MeshGroups;

    public VertexBuffer Vertices1;
    public VertexBuffer Vertices2;
    public IndexBuffer Indices1;
    public IMaterial Unk6C;
    public IMaterial Unk70;
    [SchemaField(0x80, TigerStrategy.DESTINY1_RISE_OF_IRON)]
    [SchemaField(0x78, TigerStrategy.DESTINY2_BEYONDLIGHT_3402)]
    public DynamicArray<SStaticPart> StaticParts;
    public VertexBuffer Vertices3;
    public VertexBuffer Vertices4;
    public IndexBuffer Indices2;

    [SchemaField(0xA4, TigerStrategy.DESTINY1_RISE_OF_IRON)]
    [SchemaField(TigerStrategy.DESTINY2_SHADOWKEEP_2601, Obsolete = true)]
    public IMaterial UnkA4;
    [SchemaField(TigerStrategy.DESTINY2_SHADOWKEEP_2601, Obsolete = true)]
    public Texture UnkA8; // A top down view of the terrain in-game (assuming for LOD)
}

[SchemaStruct(TigerStrategy.DESTINY1_RISE_OF_IRON, "7F1A8080", 0x60)]
[SchemaStruct(TigerStrategy.DESTINY2_SHADOWKEEP_2601, "54718080", 0x60)]
[SchemaStruct(TigerStrategy.DESTINY2_BEYONDLIGHT_3402, "866C8080", 0x60)]
public struct SMeshGroup
{
    //Location?
    public Vector4 Unk00;
    public Vector4 Unk10;
    public Vector4 Unk20;
    public uint Unk30;
    public uint Unk34;
    public uint Unk38;
    public uint Unk3C;
    public uint Unk40;
    public uint Unk44;
    public uint Unk48;
    public uint Unk4C;
    [SchemaField(0x58, TigerStrategy.DESTINY1_RISE_OF_IRON)]
    [SchemaField(0x50, TigerStrategy.DESTINY2_SHADOWKEEP_2601)]
    public Texture Dyemap;
}

[SchemaStruct(TigerStrategy.DESTINY1_RISE_OF_IRON, "481A8080", 0x0C)]
[SchemaStruct(TigerStrategy.DESTINY2_SHADOWKEEP_2601, "52718080", 0x0C)]
[SchemaStruct(TigerStrategy.DESTINY2_BEYONDLIGHT_3402, "846C8080", 0x0C)]
public struct SStaticPart
{
    public IMaterial Material;
    public uint IndexOffset;
    public ushort IndexCount;
    public byte GroupIndex;
    public byte DetailLevel;
}
