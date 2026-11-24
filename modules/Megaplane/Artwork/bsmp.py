"""Import Boscali's BSMP v2 runtime meshes in metres. Unity +Z becomes Blender +Y."""
import bpy, gzip, struct
from pathlib import Path
import numpy as np

def read_model(path, texture_dir=None):
    texture_dir = Path(texture_dir) if texture_dir else Path(path).parent
    with gzip.open(path, "rb") as src:
        def read(fmt):
            fmt = "<" + fmt
            return struct.unpack(fmt, src.read(struct.calcsize(fmt)))
        def count():
            return read("i")[0]
        def name():
            value = shift = 0
            while True:
                b = src.read(1)[0]
                value |= (b & 127) << shift
                if b < 128: break
                shift += 7
            return src.read(value).decode("utf-8")
        if read("2i") != (0x504d5342, 2): raise ValueError("BSMP v2 required")
        materials = []
        for _ in range(count()):
            label = name()
            red, green, blue, metal, gloss, alpha, tiled = read("6f?")
            m = bpy.data.materials.new(label)
            m.use_nodes = True
            m.diffuse_color = red,green,blue,alpha
            node = m.node_tree.nodes.get("Principled BSDF")
            node.inputs["Base Color"].default_value = red,green,blue,1
            node.inputs["Metallic"].default_value = metal
            node.inputs["Roughness"].default_value = 1-gloss
            node.inputs["Alpha"].default_value = alpha
            m["bsmp_tiled"] = tiled
            m["bsmp_values"] = [red,green,blue,metal,gloss,alpha]
            if label.endswith(("_Emit","_Lights","_NavRed","_NavGreen")):
                node.inputs["Emission Color"].default_value = red,green,blue,1
                node.inputs["Emission Strength"].default_value = 2
            def img_node(filename, noncolor=False):
                img=bpy.data.images.load(str(texture_dir/filename),check_existing=True)
                if noncolor: img.colorspace_settings.name="Non-Color"
                tex=m.node_tree.nodes.new("ShaderNodeTexImage"); tex.image=img
                return tex.outputs["Color"]
            if tiled:
                tex=img_node("BS_Megaplane_Panels.png")
                mix=m.node_tree.nodes.new("ShaderNodeMixRGB"); mix.blend_type="MULTIPLY"
                mix.inputs[0].default_value=1
                mix.inputs[1].default_value=red,green,blue,1
                m.node_tree.links.new(tex,mix.inputs[2]); m.node_tree.links.new(mix.outputs[0],node.inputs["Base Color"])
                bump=m.node_tree.nodes.new("ShaderNodeNormalMap")
                m.node_tree.links.new(img_node("BS_Megaplane_Panels_N.png",True),bump.inputs["Color"])
                m.node_tree.links.new(bump.outputs[0],node.inputs["Normal"])
            elif (texture_dir/(label+"_Tex.png")).exists():
                map_color = img_node(label+"_Tex.png")
                m.node_tree.links.new(map_color,node.inputs["Base Color"])
                if label.endswith("_Emit"):
                    m.node_tree.links.new(map_color,node.inputs["Emission Color"])
                    node.inputs["Emission Strength"].default_value = 1.15
            materials.append(m)
        nodes = []
        for idx in range(count()):
            label, parent = name(),count()
            x,y,z = read("3f")
            num = count()
            mesh = None
            if num:
                data=np.frombuffer(src.read(num*32), dtype="<f4").reshape((-1,8))
                p=data[:,[0,2,1]]
                norm=data[:,[3,5,4]]
                mesh=bpy.data.meshes.new(label)
                polys=[]; groups=[]
                for slot in range(count()):
                    mat,n=read("2i")
                    ids=np.frombuffer(src.read(n*4),dtype="<i4").reshape((-1,3))
                    # The handedness swap reverses winding again.
                    polys.append(ids[:,[0,2,1]]); groups.extend([slot]*len(ids))
                    mesh.materials.append(materials[mat])
                triangles=np.concatenate(polys).astype(np.int32)
                mesh.vertices.add(num); mesh.vertices.foreach_set("co",p.ravel())
                mesh.loops.add(triangles.size); mesh.loops.foreach_set("vertex_index",triangles.ravel())
                mesh.polygons.add(len(triangles)); mesh.polygons.foreach_set("loop_start",np.arange(0,triangles.size,3,dtype=np.int32))
                mesh.polygons.foreach_set("material_index",np.asarray(groups,dtype=np.int32))
                mesh.update(calc_edges=True)
                mesh.polygons.foreach_set("use_smooth",np.ones(len(triangles),dtype=bool))
                mesh.normals_split_custom_set_from_vertices(norm.tolist())
                uv=mesh.uv_layers.new(); uv.uv.foreach_set("vector",data[triangles.ravel(),6:8].ravel())
            obj=bpy.data.objects.new(label,mesh)
            bpy.context.collection.objects.link(obj)
            if parent >= 0: obj.parent=nodes[parent]
            obj.location=x,z,y
            nodes.append(obj)
        if src.read(1): raise ValueError("trailing bytes")
        return nodes
