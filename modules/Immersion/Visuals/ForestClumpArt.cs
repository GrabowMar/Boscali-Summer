using System;
using System.IO;
using System.IO.Compression;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Modules.Immersion.Visuals
{
    /// <summary>Varied mature crowns while retaining the native folded impostor and tangent frame.</summary>
    internal static class ForestClumpArt
    {
        private const int Trees = 12, VerticesPerTree = 18, IndicesPerTree = 36;
        private static readonly Vector3 NativeCenter = new Vector3(1.0097351f, 8.934727f, -2.3508387f);
        private static readonly Vector3 NativeExtent = new Vector3(24.47487f, 17.176315f, 25.55323f);
        // Width, height, relative yaw and slight lean. Keep full rounded deciduous crowns:
        // tapering a photographic impostor into a needle also stretches its photographed trunk.
        private static readonly Vector4[] Profiles =
        {
            new Vector4(1.14f, .96f, -4f, .22f), new Vector4(.94f, 1.03f, 7f, .1f),
            new Vector4(1.1f, .94f, -8f, .28f), new Vector4(.97f, 1.04f, 6f, -.2f),
            new Vector4(1.12f, .92f, -10f, .32f), new Vector4(1.09f, .96f, 9f, -.3f),
            new Vector4(.92f, 1.06f, -4f, .16f), new Vector4(1.08f, .95f, 12f, -.12f),
            new Vector4(.96f, 1.05f, -11f, .15f), new Vector4(1.1f, .92f, 8f, -.1f),
            new Vector4(1.08f, .98f, -7f, .18f), new Vector4(.95f, 1.05f, 5f, -.15f)
        };
        private static readonly int[] NativeTreeIndices =
        {
            0, 1, 2, 0, 2, 3, 0, 3, 4, 0, 4, 5,
            6, 7, 8, 6, 9, 7, 6, 10, 9, 6, 11, 10,
            12, 13, 14, 12, 15, 13, 12, 16, 15, 12, 17, 16
        };

        internal static Mesh Create(Mesh original)
        {
            if (original == null || original.name != "TreeClump1" || original.vertexCount != Trees * VerticesPerTree ||
                original.subMeshCount != 1 || original.indexFormat != IndexFormat.UInt16 ||
                original.GetTopology(0) != MeshTopology.Triangles || original.GetIndexCount(0) != Trees * IndicesPerTree ||
                original.GetIndexStart(0) != 0 || original.GetBaseVertex(0) != 0 ||
                (original.bounds.center - NativeCenter).sqrMagnitude > .0001f ||
                (original.bounds.extents - NativeExtent).sqrMagnitude > .0001f) return null;

            var vertices = new Vector3[Trees * VerticesPerTree];
            var normals = new Vector3[vertices.Length];
            var tangents = new Vector4[vertices.Length];
            var uv = new Vector2[vertices.Length];
            var indices = new int[Trees * IndicesPerTree];
            // The shipped mesh is not readable in the player. Decode the small version-guarded
            // template once per enable; no GPU readback, native mesh mutation or bitmap asset.
            using (var bytes = new MemoryStream(Convert.FromBase64String(NativeVertices), false))
            using (var gzip = new GZipStream(bytes, CompressionMode.Decompress))
            using (var reader = new BinaryReader(gzip))
                for (int i = 0; i < vertices.Length; i++)
                {
                    vertices[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    normals[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    tangents[i] = new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    uv[i] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                }
            for (int tree = 0; tree < Trees; tree++)
            {
                ShapeTree(tree, original.bounds, vertices, normals, tangents);
                for (int i = 0; i < IndicesPerTree; i++)
                    indices[tree * IndicesPerTree + i] = tree * VerticesPerTree + NativeTreeIndices[i];
            }
            var mesh = new Mesh
            {
                name = "Boscali Mixed Forest Clump", hideFlags = HideFlags.HideAndDontSave,
                indexFormat = IndexFormat.UInt16, vertices = vertices, normals = normals,
                tangents = tangents, uv = uv
            };
            mesh.SetIndices(indices, MeshTopology.Triangles, 0, false, 0);
            mesh.bounds = original.bounds;
            mesh.UploadMeshData(true);
            return mesh;
        }

        private static void ShapeTree(int tree, Bounds bounds, Vector3[] vertices, Vector3[] normals, Vector4[] tangents)
        {
            int start = tree * VerticesPerTree;
            Vector3 root = Vector3.zero;
            float bottom = float.PositiveInfinity, top = float.NegativeInfinity;
            for (int i = start; i < start + VerticesPerTree; i++)
            {
                root += vertices[i];
                bottom = Mathf.Min(bottom, vertices[i].y);
                top = Mathf.Max(top, vertices[i].y);
            }
            root /= VerticesPerTree;
            root.y = bottom;
            Vector4 profile = Profiles[tree];
            Quaternion rotation = Quaternion.AngleAxis(profile.z, Vector3.up);
            float height = top - bottom;
            float scaleY = Mathf.Min(profile.y, (bounds.max.y - bottom) / height);
            float fit = 1f;
            Vector3 min = bounds.min, max = bounds.max;
            // Fit the whole folded tree, rather than clipping individual card corners.
            for (int i = start; i < start + VerticesPerTree; i++)
            {
                Vector3 p = vertices[i] - root;
                Vector3 offset = rotation * new Vector3(p.x * profile.x + profile.w * p.y / height,
                    0f, p.z * profile.x);
                if (offset.x > 0f) fit = Mathf.Min(fit, (max.x - root.x) / offset.x);
                else if (offset.x < 0f) fit = Mathf.Min(fit, (min.x - root.x) / offset.x);
                if (offset.z > 0f) fit = Mathf.Min(fit, (max.z - root.z) / offset.z);
                else if (offset.z < 0f) fit = Mathf.Min(fit, (min.z - root.z) / offset.z);
            }
            float scaleXZ = profile.x * fit, shear = profile.w * fit / height;
            for (int i = start; i < start + VerticesPerTree; i++)
            {
                Vector3 p = vertices[i] - root;
                vertices[i] = root + rotation * new Vector3(p.x * scaleXZ + shear * p.y,
                    p.y * scaleY, p.z * scaleXZ);
                // Inverse transpose for normals; forward transform and re-orthogonalize tangents.
                // Keep native UVs and tangent handedness exactly, avoiding dark cross-card seams.
                Vector3 n = normals[i];
                n = (rotation * new Vector3(n.x / scaleXZ,
                    (n.y - shear * n.x / scaleXZ) / scaleY, n.z / scaleXZ)).normalized;
                Vector4 originalTangent = tangents[i];
                Vector3 t = rotation * new Vector3(originalTangent.x * scaleXZ + shear * originalTangent.y,
                    originalTangent.y * scaleY, originalTangent.z * scaleXZ);
                t = (t - n * Vector3.Dot(t, n)).normalized;
                normals[i] = n;
                tangents[i] = new Vector4(t.x, t.y, t.z, originalTangent.w);
            }
        }

        // Native TreeClump1, sharedassets1.assets object 802, game assembly babdd626bbab.
        // 216 little-endian vertices: position3, normal3, tangent4, UV2 (48 bytes each), gzip.
        // Only this native template's geometric/UV/tangent data is captured, not texture pixels.
        private const string NativeVertices =
            "H4sIAAAAAAAC/2WaeViNW/vH04RCKEODpChJRWiw915rP5GhzEKcQ8ZmUZmjiYpQhHAoU6aUOan2ftbelcwdHEOhTB2OeTiVmXfdq97fby+v6/Lf" +
            "97qv73Ov72fda61dzLFHxHrrQZXvSQv5np+BWBZViaJHaxdq/d+/OEz/k8wxrbD7UD3p+LvJ8i5U/+PID9XmkR/QjSHueK6WNmnoskycI7XHk7MG" +
            "MH1z1RLk16QPfHJNAP2sEcPQCOlCvK5horhw6Wsx9lYqOuE5ieljmnugNkum4xjqJ4TqmZ8vFrKqP2Px5BM2hQNKjCTgpvJnLNPXlX1Cban+/MXG" +
            "+n0KRgplg4ah/rS+TGsSmrPgNXK6kypeG9RYv8ZzCAa9PvUD39vr2Eih54QPyNbLHQ/+2Qy3bLMMhUrtycldjf5Tv43H26j/QiGI9ad6qlQe/M6I" +
            "jA2rQIG33Ylx6Cj8IDBD1g13JJr96T9uH/PT6fobVcTcDuhfvUW47bz5ovP3OhS5aL2Y7j+BaPoBPfQT9DEfFWjTJ1c8tmNn0jkoD6mrXcibcd3I" +
            "r36gPviZdn2n4uzDWOybWa5I+Gscnv+PjmSXWzui2Z+p5aPU0E/Zaqm8om6vaLEvEr9bmqUwPB6JV73ci5I87xdr9h/0ttQP6NMOOJLjVyW4uqKn" +
            "Ms5Uilf4O+LlLbsoNdc32ziIgL650xd88IkRuRZcgVo+cscbFowiOrIMpdqF709ik5+bLl/wWOrHnvqR52VJrY9Hkv0v94qvc24Wa/YnsckP6Ov2" +
            "O5Jj1M+u+fYyIzMpSfB3JHiKhVKzP+AH6oOfqK6Zilzan/A95dIVpeNI1mfd4ruj+P58EBrX690FL+GltANa2GIR7j17Psp6Xif+Nmc90g1qXK//" +
            "9qcvXS/IwwuVl3BQR4k2fHTFKuPO+MLYPNGpxgVH+DWu13/7kz/ATUip1lGrmi1VDhxzByHK14f4kSS6dCaGPAfueI00+7PoxG55MtW/CbZRtz0c" +
            "Kl6jfD08YEd8K3VwIOUr+ZghxxfoQ++eFUCfXF2iBL4ytqWI59c3xysoXxM6juT4Aj/hVA9+9lVulN4DvnQMlbcLGv3cOd/o57/9ScgpFqB+bWEr" +
            "4n5yPeOr1HGHcuv5hdiB8vXHnYMKzfWqPFTMvndbUGtSMtMB96B8tXwrEa88keG5lK/BUc9kmus1cvNgpv9wpyN5lRzA+Jp7xRY1M+kgPqR81aYv" +
            "5/KTvryYfa/TUwe16Q4jEfg6VBclzrHRJcBX25TRHF+gh36C3upMvgh8WQ0zI999rooqyldKXifyqx+oD37cz3xifHlVFUme2jYoO1C+zneP4fLz" +
            "VHVbDv1MfGwlrLvRk/ElKteidagGraR8hZRM4vID+nXUD+hf1J4ST9A8f31ugusHZqJ4ytfAXg5cfranuwqgt0VJZP5rNaoOqkCTBk/AETWzya5B" +
            "Gcq+Po+5/Hw+VMT8jEgyFU6f1mN8HXm3GS2X1orA17Y+E7n9EPRQH/StIlJF4GtJoSXu94aIKylfY4ytuf0Q/EB98LM200eaRftz1uofqfffM8nH" +
            "ngbF60z5/Myc1JifKutzRDBdz/iKuJetkFcvJNMoX5bpHWVcPic05mfAsnPk8EYXvJHy1eb9WOUNazfSm/K1qtdbiWZ/5pjqC/9Oua76ZhKvTnKv" +
            "F79FViLlv4OJ54TJLM+Dhjfn8mO115mA/sPvK9TvZzqRPylfO3NbiF2kEhwEfMU5iJr1Qb88PlMA/WyHDSLwFVs7Q9kqdiHjq+HRKKWmf/ATQ/Xg" +
            "p3zYfsUdypddtJ4yN6nRj+qzPpefuARvAeoPMVWrE+dvl7nS+tHX1ov27d6h2tuponnKRC7PVsu82fcOMVOrP87wQL0oX45pDsR90gc0hPIVZNCB" +
            "y/PEWj2mX+y7Sp1N9ohjKF+Ha37DJ3aHkSeUL68iImr2Z6hpP/a9+e/j1XdT1jO+tNr08zB/tJDxNf2JoUzTD+ihPuhrDPuRzZQv4Wa7gcbnXQnw" +
            "NbP1QNmvfqA++LlsaM/2Zz/yRhrxI4QYGLWUpFpcFjX7E7hzHOtn88jWav1Z5gj4yt+ShrqfaEYSKV+x1iM5vkD/kfoB/aKt+xHwNbeZKf42/44Y" +
            "S/nqsdKM48vKRleop/oreQnqgu9BeC3tj/PpUlnG1E3KLMcMpfv+GO78o1s9nPm5Pa6DWt9QhoAvQ4tEZJX9HWVTvtq9GsXxBXqoD/qo5RcRzNOJ" +
            "t1rjsc5nUBzly7C6O8cX+IH64Gf0zq/S47Q/1wNnSfI+YOWrJ7rFNQX8+af7SE/WT4NHCvX3JV9k0ZSvz2dXiE4N/4gulK+KQj9u/1/l5SnUUT//" +
            "Uv3GKVnoNOVr11YrEjLrjKhP+br8lyU3X+x/aqthfVMMBqmveLQgXyhf9y5J8B1X2Ge1tMrG8/PdX52mAr3Po6WqJ14tEMyvhkAnPGHdfRHm1z9e" +
            "Xbj+gx78g35f4AwZ8BXptAF1+nZNjKd82SdN49YX/EB/wI/p9ALGl//DH9LcpEY/y/7uwPGVcmyNGupv7zlOnXltowh8fRluJGtWH0UeU77a5X1Q" +
            "aq5X7dE17HvbW41T+07vQxwoX3sv5sncCtzIIMpX2LDpouZ6vXbWU3+g+gnacvUJmxD8iu7PqvofCv/tlwbOjc2QFY+K5fKz53UW+94j71NVy47d" +
            "kgFfDe1XItmqi2g+5evdgulcfkAP9UFv6ZrMzoc3L/XAniN3IZHyteYlnx/wA/XBz6eqb1KYX1vmZxdNa31RYfFMR2L1is9P1f1pKujn4uAt6kPz" +
            "RzO+smyXi2+sWxM4H66Y58XlB/SQZ9B/Naxmee40RZ/4tr4qwvy60IzPj+VRHTXwWLZJqk4fniD6Ub4mtZhB+lyLxL8BX1czuf2nsMiD+bFqsV19" +
            "fbIz48u8YI147oERhvnVOmoQtx+CHuqD/tCNIuZHt6w9KSl5BfOUBMe35/ZD8AP1wc+JmfqK3bQ/Z/zrFO33R2CjLgbFd1se5vafmfoprJ/6OT5q" +
            "V/P14mLKV4PFPsXx7AXYh/L1VH2dm18KvRSWn/g9PvTc6kIOU74e24xU1kYPwB0pXxPflsk0+3M8PJ7pLTxS5Z6iHoL5dWDTHDzLdwXLc+oqY26+" +
            "qww+Mb6qj3+WD+jQHsP8Wjf1EqorlJIQytfyT224+QV68A/63HUR7P61eqgdmmweQGIoX9Gt8kRN/+AH+gN+ZladkABfQ4PNZdNmNvpZpN2em++T" +
            "w4pYf+yadRP6vndjfGl1nYNk7weRJ5Qv3cUmXJ5fBBWx752s3024ElwjAl//7jTA2speZDDla97PY0hzvSp6xjO+xF1b5MucrPHKwAoktLUnBYEu" +
            "2GVNhsx2jyvHV7KiOfNzZPpH+Z6CeHb/ylz7WJZ7fjaOoHxJmucjTT+gZ3xR/VXT7hj48h2Yhvp07Y1hfh2YtOx//EB98CP9msf4siwrUHgbOmFL" +
            "ype1QsLx9SonhfUzrpW/at70uwrga8WxvaIivj0Cvn4+n8fxBXrgC/SJ5UFKyHPK747kbFdjRO87+EyRO8eXZ2UM48tn/jb56hwfMoXyVb/2mdj1" +
            "5ms0i/I18vUMbn/uXLOG+amoSlJNaZOoBL5Kb+4SF1vqsfNhXsB8ji/QQ33Q33rQmp0Prcb2Io+d1yrh/lW1TsLxBX6gPvjJ3n2X8TWsX5xi15ha" +
            "dMvCoLijzRxuf84KOMv6GdXNVhgb7Mf4Shzqipw2CNib8uVT24nb/51nnWX5OWBrK+yvbcH4GnX9KTJ0McYmlC+Duiou/0dHn2C8zLoULLTqNRt/" +
            "p3wF6/cUC/08EOR51bloLj+ouRXTjzIqkQcOJwj42lPWgTh5/K0MpnztfCPj+g968A96taUl42tq9mLR9dQOEfhycw7m1hf8QH/Az+QQM/a+8TDt" +
            "cXHcI3fm5+elaC4/07scRVD/hX2usKMyFAFfK6XOqPNJcwzzayJGHF9+xUcR+J9hmyucz2uD4Xwoe/QXkh1SIODLZLwdNy9u1x1m88XTPFhAP5zI" +
            "OJofSYk5uXTHGd+k58NFph5cfs7+bc2+d0talXxvQwWbX6Zkpfjx3D72vqGrHcTlB/RQH/RL7ySw+bUvx5Z8mT2dvW8Y+TmRX/1AffAzyoOw8+Hj" +
            "tgcUO1P64y5tWkpKYwZw+VGSiSxvx2PjhASjxveNP252kdX7h7P3jfvjXbn9DfTAF+h1TvZm7xs17odkph5umJ7HcNhRJy4/UwMPsvlitGWO8NS+" +
            "UmwdUoEG3B2NK1wDyGT6vvGz7D43v8oWzWR+ym/FCuOXZrH7l87fO2V7v4Wz9400PXtu/wE91Ad9Xk1P9r7xaIgdWlI2kL1v9BZKufMz+IH64MfD" +
            "OExxmvan7v0DqeupYEIuaRdH/7zGza9t22wx9PObYY5wRo4Q8KWYNxbVNe9KxlC+qpUSjq+4Z7YY8hPZIkfY+ewd2kv56rFMF3e8USt2oXwN0THi" +
            "zm/WY3uy86S+RYCQ4eOEyylflV1M8cxPPdm82DtGzvFlfDhdgDzo35IIcTf6YHg/fDB6vfJZtS0JoHxZeYlIsz7o2fyl+o5O6cib5v/qX88VS3wC" +
            "yWrK1/2tB5Cmf/AD/QE/bsdeSWF+nZlSITXc2ujn+345x1dkQBtWv0Z1Sqgqby51p/UXVaWjF5fGi2mUr09t53N8DQ5vw753P9Vv63RA2ofyZbK3" +
            "L+78bKNoRfnadrwvx9eCNDt2/0o5HyxEyafJ4HxYcz+YHJTE4HB6PlzrXizT7E9CyjbmRzlCImyJTmbzq82TfcofNUE4ivJleiVL1PQDeqgPeld/" +
            "Bza/XoaPFr13O2Dgq6woVvzVD9QHP/08yEDga/i3b4q60TE4m/JFFC9kmv3RMg6RQz9ff2kp3C4IZHyZGQSIF0LdMLxv5L6y5PY30EOeQT/8TYMI" +
            "fLl3qxdHlRpjOB/izOfc+ibfc2D3Ndmz2UJD1wAyke4/W1Oc0fJT/uJ0Or/kFku5/cdvTDTzE2WsJ/zxPo7xtcJzmCjpM4AconyZLO7G7T+gh/qg" +
            "Tw1tyfiKmHZLjLilT+B9Q5aow+0/4Afqgx8by8+Ko7Q/YTX5kqdJS8Vjr3WLJw9bwu0/wgwj1s+4rEJBGjpQCXydTlmLPu4ZhGB+hVhHcHzdDzZi" +
            "6yWn+gPeL5Uwv/pm9cFbT/VAML+EABeOr2s7MoWS+jmqBk9LdfBoWyxSvszV3YhVtAt7T7iY6MbxtSkkXRCpPlW1SN22fYTiOuVry+G+pFPaPjSb" +
            "8rXgqiM3v0C/3TtMAP1q7QvFML9MH6aLL6vjEcyv7uZzufUFPzupHvy8kz+U3qJ8JVy9p9h1r9GP2s6N46sg8SKr/26Gntq8OB0BX+sTg6SWzqE4" +
            "gfI11j6N2w/PUT1875RAPfWamL64L+UrLN9Q9ibcAZtRvu4OyePOYxHbG/vjJ7dUF7YJIi8oXwG7tcTct71RGOXrQVY0l5/JTd8bQr/X8kWEEvjy" +
            "GpIiDjoZJcL5cIR/OJefyU39BP22bqbs/fDJMycytt9wEfhSmvUhv/qB+uCn5ZCvbH5dVa8obsgejDB93+gkjebyIy1fyvqp565UjVy6mPG1+Kcv" +
            "qr7cm/GVFtKLyw/oy6kf0Cvu6hB430j4+hgN1dXC8L7Rt5cBl58Sul6gT5FZqmMfXpWlU7723Q3CvxUuJ0coX3ObtePuF8lNfvq7KVU7b/3G+DII" +
            "jUCJds5sflV9t+P2w+QmP6BvIXnBeI/ar4X75rYm8L4h4E/cepU05Qf8WGcNkuyl/Vkr+SYdtHo5MTM2KFbV23H3i+NN+VlO8/N9+Bq0gvJVmrla" +
            "Vr4hjNhSvjK8krn5Ljbl52mAnlqr3BEXUL6kGb3R+x1ORJvyVdJmKjffN/d+R6zzp6t8wxTq3A4eJJnyZfrYkIyaZsbyfMJ9BJcfB2+lypLqN587" +
            "qvY1dCYwv8Z9rJPphHbF8PtX6YjbXH3QrwoZJIA+Lb/x/bDsD2/Z9ZTpGH7/arC/wPkHP0lUD35+6H1RAF8RWqJi/LFGP1O++nD5eXw2h0D9ZMkD" +
            "tW3SOwXwlW+TLt7Y2wp1pXxZjI7i8vy6Kod9ryfV93y7UelK+bI714dcE8PRF4k9+ZjuyuV5UOfG/uSFKNQLlROlcP9aKw/B9YNiyTB6/8oZulmh" +
            "2Z/K4Y3fG0q/V2q2ir0fXh5xWXYoYCaB943KLBXHO+ihn6DX72xHgK8j2XFox0JbAnxNKt2GfvUD9cHP1eiggQfg9y/5d+mcy7Gkax9DSdJdG6Vm" +
            "f7R3/Imhn5GDD6rHai9h7xtP/H3R9Z2eBPgyadaO4wv0ttQP6Hv10MWQ566jHqMOdeZwHsNO+C9ufdcaviOg307Xa9XQmXgJPR8+zo8Ux/TajBro" +
            "+XDW+AXc/tw/s9HPZlo/rD6ZvW+4qvqjvelyfJDydf6UMccX6KE+6J/XtsUwv/7epEJ9urfFML9qYz5w/QE/UB/8OLaql56k/ZmS86M4rX43Glal" +
            "W9xdL4rbnzufaczPGpoHs21/SmMpX0kP14n1vp+UbSlfnU0WcPt/tzv/n5+3BQmyC5SvuSNdyOXVj5Tvq13wiYb+3HwxrspW20r9VW3HPVD12e9P" +
            "FlK+0MVFaKXNahHyfP3CIo6vzW9zmf7PH16qoGXZ7PevZsld8KdOchmcD7NPY25/A315nZsA+kt1zUU4H/aUJqDqn7NRMuWr0nget77g5zzVgx+v" +
            "2Z2UwNfLTFup68tk5if92iKOrwMFxaz+pxIL9fcBy0QPWv90czPx2UEHUncrVYz66szxtf1sMfOfQyzU//zeibhRvt67F4nNTarEV5Qvj2WWHF8O" +
            "dxr7Y0z9rH4uYe8bcf8Y4pEtu5JelK8ehUO5/AQ3fW8q/V6Z9242v6aqktGsmIVsfl0Qwrn8BDf1E/Qn93k3zq8iB3pntmbzq9VGF/yrH6gPfqqn" +
            "XpUCX5umbZaerLAhKc6Gks9Fg7n8uD9OZP2sELXU9kW7GF8pRslK87hg9j6/wXcVdz4HvQP1A/qpA3oxvp68MxKXF7qQGMpXxq253PnQsjKb6XP9" +
            "HqhMLE+hSjq/ei/zI9frwrGOU4byy7VT3PxCTX7siJb68YI/GF8VMX8pJwSE4AOUrzlZ0dz5GTX5Ab1XRnfGV+zCGeK1N25wXiWnR1lz52fwA/XB" +
            "z2SpF+PL2PG5otufETjzlm5xbudD3PxKbcrPwFILdXnZaBH4atFeLg4rd8KdKF+znRw5vuKb8vOK5sdmtQ6poHwN/fhBbH//G3pO+bodr8vxdQVt" +
            "ZHq7iM1Cv41jFYspX08/B+L5n2PZfaeyNpDbn7V9Q5ne8u9s4bunFns/1PpWj5bm9yfwPt/WI4Hb30AP/kFv29GX3b+2TfJBd9dOYPcvm4x/ufkF" +
            "flj/qR/nKxWFf1G+Dmt3kfX52uhnUvoQhWZ/TL3zWH1V52xB/+ZkxldPZy906oMf+Uj5OoefcfOifFge829lli3I83WIO+UrY8cbdCrWlbynfC3O" +
            "mcfth4Ob+jNu3mah7b2BeBXl6+2Y1mT0aGP2fngzcQzHV9X4xu/9XJstbBoazu5f9253Rq13jMfwvvHF8CvnB/RQH/Q7Czuy+9eCYWr0W3srdv96" +
            "/u3E//iB+uAn/eYF9n4o+bRdMculDf7+VEei+9CX4+vepQ2sn/X7gwTt3aVS4Cv8yl6xf3JNMcwvHfp3HJr9Bz3kGfQvZ4+RAV/roh2Jl7xWCvev" +
            "EHcp/z4v38j056mfB9dcyQ3K12+x7YlI7/BWlC+bkfz5p/nlRj/PaH2DH5EK4GuwyT4x7+4+KfBVsi+S2w9BD/VBX3u9mRL4MrngSBSSoTKYXwnv" +
            "+fdD8AP1wc/trDLFGdof8vsuRZi+Cc4/qV08pHoMtz+7NOUni+bHcUUk40v7ajsU0G0Sbkf5Onz1DZfPh0350aL5eRFoyvgKzDmLYjZY49eUr4un" +
            "D/HzfeIRYVP6DJWPUaiwNm4w3kD5WnS4QawwacHOY4a3/bj8+H07LKRRfcdl3YQiS33291GHEpxIa+SE4Hy4x1jC9R/0x3t4C6APODKV/f613WuD" +
            "GOU5VQa/f62KXMitL/g5SfXg588cXRnMr6K6gwqzpEY/5al+XH4+P7jM6ncqTRJQ3XoEfJVN2Sqb/mki7k3Ph4WJb7g8L314mX1vgypJCKZzYgDl" +
            "a+p9V5QT3QE3o/evMfL3XJ4X+B5h35vWNlQ45vg7iaN8HemwWyx9r0TdKF++38K4/Ng2fa8V/d6HrXYUAV/J9HfmBd5mSuBrReVCLj+2Tf0EvfvQ" +
            "EcXAVzj9nVm7ZLCyhPJ1v5g/r4IfqA9+erZ5y+5fX3zsFDmzLqIr9H1DOjWUy0+Y7TrWz2dma4TDI9LY/evmPAu049AInED56hWhzZ0fQA/9Af3m" +
            "U53Z++Gr/APoRRcz9n4oT6/k8mPYlJ9U41DB/ZFCWUD5GmASjA3PxJA39P7l8c9apWZ/4pv8dDZfI0hLV7H71wBdhKro31PB78tb7H5y6xXf5Af0" +
            "uYtas/eNhsRLyD+5O3vfMJldwK2XYVN+wM9fYwOLs2l/HohfpVcLY8kpA4PisxNl3Pn5RVN+BtP8NDjGIODLaOcnWYbtZNKF8rXD/xnHV2hTfmzU" +
            "ScK2R1a4nPJ1sXcmsrbvSv6lfPX3yOf68x81pWwDgCgAAA==";
    }
}
