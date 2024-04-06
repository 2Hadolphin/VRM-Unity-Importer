using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Serialization;
using UnityEditor;
using Object = UnityEngine.Object;
using Mesh = UnityEngine.Mesh;


using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities.Editor;
using FilePathAttribute = Sirenix.OdinInspector.FilePathAttribute;

using VrmLib;
using UniGLTF;
using UniVRM10;

namespace Return.Editors
{
    /// <summary>
    /// Toolkit to import and initialize VRoid character.
    /// </summary>
    public partial class VRMImporter : OdinEditorWindow
    {
        [MenuItem(MenuUtil.Tool_Asset + "VRM Importer")]
        static void OpenWindow()
        {
            var window = GetWindow<VRMImporter>();
            window.SetOdinContent("VRM Importer", SdfIconType.ArrowDown);
        }

        #region Import

        [InfoBox("Import .vrm file into scene view.")]
        [TabGroup("Import")]
        [FilePath(AbsolutePath = true, Extensions = ".vrm", IncludeFileExtension = true)]
        [SerializeField]
        string FilePath;

        [TabGroup("Import")]
        [Button]
        void ImportVRM()
        {
            if(File.Exists(FilePath))
                Import(FilePath);
        }

        #endregion


        #region Archive

        [InfoBox("Archive content as persistent assets.")]
        [TabGroup("Archive")]
        [FolderPath]
        [SerializeField]
        string ArchivePath;
        [TabGroup("Archive")]
        [SerializeField]
        GameObject Target;
        [TabGroup("Archive")]
        [Button]
        void SaveAssets()
        {
            // texture => material => mesh
            SaveMaterials();
            SaveMeshes();
        }

        [TabGroup("Archive")]
        [Button]
        void SaveMaterials()
        {
            var folder_texture = Path.Combine(ArchivePath, "Textures");
            var folder_material = Path.Combine(ArchivePath, "Materials");

            if (!Directory.Exists(folder_texture))
                folder_texture = ArchivePath;

            if (!Directory.Exists(folder_material))
                folder_material = ArchivePath;


        }

        [TabGroup("Archive")]
        [Button]
        void SaveMeshes()
        {
            var folder_mesh = Path.Combine(ArchivePath, "Meshes");

            if (!Directory.Exists(folder_mesh))
                folder_mesh = ArchivePath;

            SaveMeshes(Target, folder_mesh);
        }


        #endregion

        #region Convert Texture

        [PropertyOrder(1)]
        [TabGroup("Convert")]
        [Tooltip("Material to update reference after converting texture format.")]
        [SerializeField]
        List<Material> ModifyMats;

      

        [Tooltip("Convert selected texture into png format.")]
        [TabGroup("Convert")]
        [Button]
        void ConvertPng()
        {
            if (ModifyMats.IsNullOrEmpty())
            {
                if (!EditorUtility.DisplayDialog("Check", "Without assign modifyMats, materials will lose texture reference(Artifact Type) after convertion.\nAre you going to contiune?", "yes", "no"))
                    return;
            }

            var textures = Selection.objects.Where(x => x is Texture2D).Select(x => x as Texture2D).ToArray();
            ConvertTextures(textures,ModifyMats.ToArray());
        }

       
        #endregion
    }

    partial class VRMImporter // Extension
    {
        #region Import

        public static async void Import(string filePath)
        {
            var src = new FileInfo(filePath);
            var instance = await Vrm10.LoadPathAsync(filePath, true);

            var exportedBytes = Vrm10Exporter.Export(instance.gameObject);

            // Import 1.0
            var vrm10 = await Vrm10.LoadBytesAsync(exportedBytes, false);
            var pos = vrm10.transform.position;
            pos.x += 1.5f;
            vrm10.transform.position = pos;
            vrm10.name = vrm10.name + "_Imported_v1_0";

            // write
            var path = Path.GetFullPath("vrm10.vrm");
            Debug.Log($"write : {path}");
            File.WriteAllBytes(path, exportedBytes);
        }

        static void Printmatrices(Model model)
        {
            var matrices = model.Skins[0].InverseMatrices.GetSpan<System.Numerics.Matrix4x4>();
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < matrices.Length; ++i)
            {
                var m = matrices[i];
                sb.AppendLine($"#{i:00}[{m.M11:.00}, {m.M12:.00}, {m.M13:.00}, {m.M14:.00}][{m.M21:.00}, {m.M22:.00}, {m.M23:.00}, {m.M24:.00}][{m.M31:.00}, {m.M32:.00}, {m.M33:.00}, {m.M34:.00}][{m.M41:.00}, {m.M42:.00}, {m.M43:.00}, {m.M44:.00}]");
            }
            Debug.Log(sb.ToString());
        }

        #endregion


        #region Archive Assets

        public static void SaveMaterials(GameObject root, string folderPath_texture, string folderPath_material)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var mats = renderers.SelectMany(x => x.sharedMaterials);
            SaveMaterials(folderPath_texture, folderPath_material, mats.ToArray());
        }

        /// <summary>
        /// Save materials and dependcy textures.
        /// </summary>
        /// <param name="folderPath_texture">Folder to save textures.</param>
        /// <param name="folderPath_material">Folder to save materials.</param>
        /// <param name="mats">Materials to archive.</param>
        public static void SaveMaterials(string folderPath_texture, string folderPath_material, params Material[] mats)
        {
            foreach (var mat in mats)
            {
                var matName = mat.name.Replace(" (Instance)", string.Empty);

                Shader shader = mat.shader;

                for (int i = 0; i < shader.GetPropertyCount(); i++)
                {
                    string propertyName = shader.GetPropertyName(i);
                    var propertyType = shader.GetPropertyType(i);

                    if (propertyType == UnityEngine.Rendering.ShaderPropertyType.Texture)
                    {
                        var texture = mat.GetTexture(propertyName);

                        if (!texture.IsPersistentAsset() && texture is Texture2D target)
                        {
                            // save as png
                            //if (texture.name.Contains("mainTex"))
                            //{
                            //    EditorAssetsUtility.Archive(target, $"{matName}{propertyName}.asset", false, ArchivePath);
                            //}
                            //else
                            {
                                EditorAssetsUtility.Archive(target, $"{matName}{propertyName}.asset", false, folderPath_texture);
                            }
                        }
                    }
                }

                if (!mat.IsPersistentAsset())
                    EditorAssetsUtility.Archive(mat, matName, false, folderPath_material);
            }
        }

        public static void SaveMeshes(GameObject root, string folderPath)
        {
            // save filters
            {
                var filters = root.GetComponentsInChildren<MeshFilter>(true);
                var meshes = filters.Select(x => x.sharedMesh);
                SaveMeshes(folderPath, meshes.ToArray());
            }

            // save skinneds
            {
                var skinneds = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                var meshes = skinneds.Select(x => x.sharedMesh);
                SaveMeshes(folderPath, meshes.ToArray());
            }
        }

        public static void SaveMeshes(string folderPath_mexh, params Mesh[] meshes)
        {
            foreach (var mesh in meshes)
            {
                if (!mesh.IsPersistentAsset())
                    EditorAssetsUtility.Archive(mesh, mesh.name, false, folderPath_mexh);
            }
        }

        #endregion

        #region Convert Texture

        /// <summary>
        /// Convert selected texture into png format.
        /// </summary>
        public static void ConvertTextures(Texture2D[] textures, Material[] materials)
        {
            DictionaryList<string, ConvertBinding> datas;

            {
                var cache = materials.SelectMany((mat) => ConvertBinding.GetData(mat)).ToArray();
                datas = new(cache.Length, 5);
                foreach (var data in cache)
                {
                    if (data.Texture == null)
                    {
                        Debug.Log($"Ignore null field [{data.id}]", data.mat);
                        continue;
                    }

                    datas.Add(data.Texture.name, data);
                }
            }

            var length = textures.Length;
            var targetPaths = new string[length];

            // clear texture reference at materials
            foreach (var texture in textures)
            {
                if (datas.TryGetValues(texture.name, out var cache))
                {
                    foreach (var data in cache)
                    {
                        data.SetTexture(null);
                    }
                }
            }

            // overwrite image format
            try
            {
                for (int i = 0; i < length; i++)
                {
                    var texture = textures[i];

                    var filePath = texture.GetIOPath();
                    var metaPath = texture.GetMetaPath();

                    byte[] bytes = texture.EncodeToPNG();
                    System.IO.File.WriteAllBytes(filePath, bytes);

                    // change extension
                    var newPath = Path.ChangeExtension(filePath, ".png");
                    targetPaths[i] = EditorPathUtility.GetEditorPath(newPath);

                    File.Move(filePath, newPath);

                    // change .meta
                    {
                        var newMetaPath = newPath + ".meta";
                        File.Move(metaPath, newMetaPath);

                        // clear importer - read 2 line only
                        {
                            string version = "fileFormatVersion: 2";
                            string guid = "guid: ";

                            // clear importer - read 2 line only
                            using (var fs = new FileStream(newMetaPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                            {
                                using (var sr = new StreamReader(fs))
                                {
                                    version = sr.ReadLine();
                                    guid = sr.ReadLine();
                                }
                            }

                            File.WriteAllLines(newMetaPath, new string[] { version, guid });
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            // import setting 
            {
                foreach (var assetPath in targetPaths)
                    AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

                foreach (var assetPath in targetPaths)
                {
                    AssetDatabase.ClearImporterOverride(assetPath);
                    AssetDatabase.SetImporterOverride<TextureImporter>(assetPath);

                    if (AssetImporter.GetAtPath(assetPath) is TextureImporter textureImporter)
                    {
                        if (assetPath.Contains("normal"))
                            textureImporter.textureType = TextureImporterType.NormalMap;
                        else
                            textureImporter.textureType = TextureImporterType.Default;

                        textureImporter.textureShape = TextureImporterShape.Texture2D;

                        textureImporter.isReadable = true;
                        textureImporter.mipmapEnabled = false;


                        textureImporter.SaveAndReimport();
                    }
                    else
                    {
                        Debug.Log(AssetImporter.GetAtPath(assetPath));
                    }
                }
            }


            AssetDatabase.Refresh();

            // assign new texture (converting native asset to foreign asset break serialize reference(Artifact Type))
            {
                foreach (var assetPath in targetPaths)
                {
                    var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);

                    if (texture == null)
                    {
                        Debug.LogError($"[Null] {assetPath}");
                        continue;
                    }

                    if (datas.TryGetValues(texture.name, out var cache))
                    {
                        foreach (var data in cache)
                        {
                            data.SetTexture(texture);
                            data.mat.Dirty();
                        }
                    }
                    else
                    {
                        Debug.LogException(new KeyNotFoundException($"Failure to find texture reference."), texture);
                    }
                }
            }

            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Cache texture binding for format convertion.
        /// </summary>
        class ConvertBinding
        {
            public Material mat;
            public int id;
            public Texture2D Texture;

            public void SetTexture(Texture2D texture)
            {
                mat.SetTexture(id, texture);
            }

            public static IEnumerable<ConvertBinding> GetData(Material mat)
            {
                Shader shader = mat.shader;

                for (int i = 0; i < shader.GetPropertyCount(); i++)
                {
                    var propertyType = shader.GetPropertyType(i);

                    if (propertyType == UnityEngine.Rendering.ShaderPropertyType.Texture)
                    {
                        var id = shader.GetPropertyNameId(i);

                        var data = new ConvertBinding()
                        {
                            id = id,
                            mat = mat,
                            Texture = mat.GetTexture(id) as Texture2D,
                        };

                        yield return data;
                    }
                }
            }
        }


        #endregion
    }

}