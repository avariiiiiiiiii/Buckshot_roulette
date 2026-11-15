"""Handcuffs asset, run in Blender through MCP. Metres; Blender X/Z is the cuff face."""
import bpy, bmesh, json, math, random
from pathlib import Path
from mathutils import Vector, Matrix

ROOT = Path(r'C:\Users\admin\Documents\GitHub\Mods\Buckshot_roulette')
OUT = ROOT / 'assets/models'
scene = bpy.data.scenes.get('Handcuffs_Workshop') or bpy.data.scenes.new('Handcuffs_Workshop')
bpy.context.window.scene = scene
if scene.world is None:
    scene.world = bpy.data.worlds.new('Workshop World')
scene.world.use_nodes = True
scene.world.node_tree.nodes['Background'].inputs['Color'].default_value=(0.14,0.16,0.19,1)
if scene.camera is None:
    camera=bpy.data.objects.new('Workshop Camera',bpy.data.cameras.new('Workshop Camera'))
    scene.collection.objects.link(camera)
    scene.camera=camera
scene.camera.data.type='ORTHO'
for name,location,size in [('Key',(-0.3,-0.4,0.8),0.8),('Fill',(0.4,0.2,0.4),0.6)]:
    lamp=scene.objects.get(name)
    if lamp is None:
        lamp=bpy.data.objects.new(name,bpy.data.lights.new(name,'AREA'))
        scene.collection.objects.link(lamp)
    lamp.location=location
    lamp.rotation_euler=(-Vector(location)).to_track_quat('-Z','Y').to_euler()
    lamp.data.shape='DISK'; lamp.data.size=size
for old in list(bpy.data.collections):
    if old.name == 'Handcuffs_Asset':
        for obj in list(old.objects):
            bpy.data.objects.remove(obj, do_unlink=True)
        bpy.data.collections.remove(old)
asset = bpy.data.collections.new('Handcuffs_Asset')
scene.collection.children.link(asset)
parts = []

# The same tiny atlas / hard pixel wear treatment as the hacksaw.
rng = random.Random(1024)
image = bpy.data.images.new('handcuffs_atlas', width=64, height=64, alpha=False)
pixels = []
for y in range(64):
    for x in range(64):
        block = random.Random((x//2)*917+(y//2)*131).uniform(-0.055, 0.055)
        grain = rng.uniform(-0.018, 0.018)
        if y >= 32 and x < 32: base = (0.60,0.63,0.66)
        elif y >= 32: base = (0.79,0.81,0.83)
        elif x < 32 and y >= 16: base = (0.34,0.37,0.40)
        elif x >= 32 and y >= 16: base = (0.10,0.115,0.13)
        elif x < 32: base = (0.66,0.075,0.055)
        else: base = (0.035,0.04,0.045)
        pixels.extend([max(0,min(1,c+block+grain)) for c in base]+[1])
for i in range(80):
    x=rng.randrange(1,30); y=rng.randrange(33,63)
    for dx in range(rng.randrange(1,5)):
        xx=min(31,x+dx)
        at=(y*64+xx)*4
        pixels[at:at+3] = [0.80,0.82,0.83] if i%3 else [0.39,0.42,0.45]
image.pixels.foreach_set(pixels)
image.filepath_raw=str(OUT/'handcuffs.png'); image.file_format='PNG'; image.save(); image.pack()
material=bpy.data.materials.new('HandcuffsAtlas'); material.use_nodes=True
shader=material.node_tree.nodes.get('Principled BSDF')
shader.inputs['Roughness'].default_value=0.65
shader.inputs['Metallic'].default_value=0.0
tex=material.node_tree.nodes.new('ShaderNodeTexImage'); tex.image=image; tex.interpolation='Closest'
material.node_tree.links.new(tex.outputs['Color'],shader.inputs['Base Color'])
zones={'steel':(1,33,30,30),'edge':(33,33,30,30),'shadow':(1,17,30,14),'dark':(33,17,30,14),'red':(1,1,30,14),'black':(33,1,30,14)}

def finish(obj,zone='steel'):
    if obj.name not in asset.objects:
        for collection in list(obj.users_collection): collection.objects.unlink(obj)
        asset.objects.link(obj)
    obj.data.materials.clear(); obj.data.materials.append(material)
    obj.data.update()
    uv=obj.data.uv_layers.new(name='Atlas')
    for poly in obj.data.polygons:
        n=poly.normal
        chosen=zone
        if zone=='steel' and abs(n.y)<0.4: chosen='edge' if n.z>0.1 else 'shadow'
        bx,by,w,h=zones[chosen]
        ax,ay=(0,2) if abs(n.y)>0.6 else ((0,1) if abs(n.z)>0.6 else (2,1))
        for li in poly.loop_indices:
            co=obj.data.vertices[obj.data.loops[li].vertex_index].co
            u=(co[ax]*160+7.5)%max(1,w-2)
            v=(co[ay]*160+4.5)%max(1,h-2)
            uv.data[li].uv=((bx+1+u)/64,(by+1+v)/64)
        poly.use_smooth=False
    obj['atlas_zone']=zone
    parts.append(obj)
    return obj

def mesh(name,verts,faces,zone='steel'):
    data=bpy.data.meshes.new(name); data.from_pydata(verts,[],faces); data.update()
    bm=bmesh.new(); bm.from_mesh(data); bmesh.ops.recalc_face_normals(bm,faces=bm.faces); bm.to_mesh(data); bm.free()
    obj=bpy.data.objects.new(name,data); asset.objects.link(obj)
    return finish(obj,zone)

def box(name,location,size,zone='steel',bevel=0.001):
    bpy.ops.mesh.primitive_cube_add(size=1,location=location)
    obj=bpy.context.object; obj.name=name; obj.dimensions=size
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    if bevel:
        mod=obj.modifiers.new('Cut corners','BEVEL'); mod.width=bevel; mod.segments=1
        bpy.ops.object.modifier_apply(modifier=mod.name)
    return finish(obj,zone)

def pin(name,location,radius,depth,zone='shadow'):
    bpy.ops.mesh.primitive_cylinder_add(vertices=8,radius=radius,depth=depth,location=location,rotation=(math.pi/2,0,0))
    obj=bpy.context.object; obj.name=name
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    return finish(obj,zone)

def cuff_band(name):
    # Broad, flat, bevelled steel strap; deliberately faceted, not a round torus.
    profile=[(0.005,-0.009),(0.007,-0.007),(0.007,0.007),(0.005,0.009),
             (-0.005,0.009),(-0.007,0.007),(-0.007,-0.007),(-0.005,-0.009)]
    verts=[]; faces=[]
    for i in range(16):
        a=2*math.pi*i/16
        for radial,depth in profile:
            verts.append(((0.043+radial)*math.cos(a),depth,(0.056+radial)*math.sin(a)))
    for i in range(16):
        for j in range(8):
            faces.append((i*8+j,((i+1)%16)*8+j,((i+1)%16)*8+(j+1)%8,i*8+(j+1)%8))
    return mesh(name,verts,faces)

def link(name,x,rotate=False):
    verts=[]; faces=[]
    for i in range(8):
        a=2*math.pi*i/8
        for j in range(4):
            b=2*math.pi*j/4+math.pi/4
            v=Vector(((0.014+0.003*math.cos(b))*math.cos(a),0.003*math.sin(b),(0.007+0.003*math.cos(b))*math.sin(a)))
            if rotate: v=Matrix.Rotation(math.pi/2,3,'X')@v
            verts.append((v.x+x,v.y,v.z+0.01))
    for i in range(8):
        for j in range(4): faces.append((i*4+j,((i+1)%8)*4+j,((i+1)%8)*4+(j+1)%4,i*4+(j+1)%4))
    return mesh(name,verts,faces)

for side,center in [('L',-0.10),('R',0.10)]:
    inward=1 if side=='L' else -1
    start=len(parts)
    cuff_band('Cuff_'+side+'_Frame')
    box('Cuff_'+side+'_Lock',(inward*0.044,0,0.01),(0.027,0.026,0.038))
    # Dark inset, exposed keyhole and a small red locking catch echo the hacksaw's red hardware.
    box('Cuff_'+side+'_Inset',(inward*0.044,-0.0137,0.007),(0.020,0.0014,0.027),'shadow',0)
    box('Cuff_'+side+'_Release',(inward*0.044,-0.015,0.022),(0.018,0.002,0.005),'red',0.0006)
    pin('Cuff_'+side+'_Keyhole',(inward*0.044,-0.0148,0.009),0.0035,0.001,'black')
    box('Cuff_'+side+'_KeySlot',(inward*0.044,-0.015,0.0045),(0.0028,0.001,0.007),'black',0)
    pin('Cuff_'+side+'_Hinge',(-inward*0.041,0,-0.014),0.007,0.024)
    pin('Cuff_'+side+'_HingeCap',(-inward*0.041,-0.013,-0.014),0.0045,0.002,'edge')
    box('Cuff_'+side+'_PinSlot',(-inward*0.041,-0.0144,-0.014),(0.005,0.001,0.0015),'black',0)
    for i in range(5):
        a=math.radians(18+i*8)
        tooth=box('Cuff_'+side+'_Ratchet_'+str(i),(inward*0.045*math.cos(a),-0.010,0.057*math.sin(a)),(0.005,0.0012,0.0012),'shadow',0)
        tooth.rotation_euler.y=-inward*a
    for obj in parts[start:]: obj.location.x+=center
    # Eyebolt at the inner side of the lock, where the first chain link passes through.
    eye=link('Cuff_'+side+'_Swivel',center+inward*0.061,True)
    eye.scale=(0.5,0.85,0.85)
    # Scale about its own centre, not the scene origin.
    for v in eye.data.vertices: v.co.x-=center+inward*0.061
    eye.location.x=center+inward*0.061

for i,x in enumerate([-0.0315,-0.0105,0.0105,0.0315]): link('Chain_Link_'+str(i+1),x,i%2==1)

# Export the exact Blender geometry, normals and atlas UVs to the existing Unity loader format.
bpy.context.view_layer.update()
positions=[]; normals=[]; uvs=[]; triangles=[]; keymap={}
for obj in parts:
    me=obj.data; me.calc_loop_triangles(); uv=me.uv_layers.active.data
    for triangle in me.loop_triangles:
        ids=[]
        for li in triangle.loops:
            vi=me.loops[li].vertex_index
            point=obj.matrix_world@me.vertices[vi].co
            normal=(obj.matrix_world.to_3x3().inverted().transposed()@me.corner_normals[li].vector).normalized()
            p=(-point.x,point.z,-point.y); n=(-normal.x,normal.z,-normal.y); texco=uv[li].uv
            key=tuple(round(x,6) for x in (*p,*n,*texco))
            if key not in keymap:
                keymap[key]=len(positions)//3; positions.extend(p); normals.extend(n); uvs.extend(texco)
            ids.append(keymap[key])
        a,b,c=[Vector(positions[j*3:j*3+3]) for j in ids]
        n=Vector(normals[ids[0]*3:ids[0]*3+3])
        if (b-a).cross(c-a).dot(n)<0: ids[1],ids[2]=ids[2],ids[1]
        triangles.extend(ids)
data={'positions':[round(v,6) for v in positions],'normals':[round(v,5) for v in normals],
      'uvs':[round(v,6) for v in uvs],'submeshes':[{'material':'handcuffs','color':[1,1,1],
      'metallic':0.0,'smoothness':0.35,'texture':'handcuffs.png','triangles':triangles}]}
(OUT/'handcuffs.json').write_text(json.dumps(data,separators=(',',':')),encoding='utf-8')

reference=bpy.data.objects.get('Hacksaw')
if reference: reference.hide_render=True; reference.hide_set(True)
scene.render.engine='CYCLES'; scene.cycles.samples=32; scene.cycles.device='CPU'
scene.render.resolution_x=1200; scene.render.resolution_y=800; scene.render.resolution_percentage=100
scene.view_settings.view_transform='Standard'; scene.view_settings.look='Medium High Contrast' if 'Medium High Contrast' in [i.identifier for i in scene.view_settings.bl_rna.properties['look'].enum_items] else 'None'
scene.camera.location=(0.17,-0.60,0.38)
scene.camera.rotation_euler=(Vector((0,0,0.005))-scene.camera.location).to_track_quat('-Z','Y').to_euler()
scene.camera.data.ortho_scale=0.39
scene.render.filepath=str(ROOT/'art/previews/handcuffs.png')
scene.world.node_tree.nodes['Background'].inputs['Strength'].default_value=0.45
for obj in scene.objects:
    if obj.type=='LIGHT': obj.data.energy=12 if obj.name=='Key' else 6
bpy.ops.render.render(write_still=True)

text=bpy.data.texts.get('handcuffs_pipeline.py') or bpy.data.texts.new('handcuffs_pipeline.py')
text.clear(); text.write((ROOT/'art/handcuffs_pipeline.py').read_text(encoding='utf-8'))
scene['asset_notes']='Hacksaw-matched pixel steel, lock housings, keyholes, ratchet teeth, hinges, four articulated chain links. Metres.'
bpy.data.libraries.write(str(ROOT/'art/handcuffs.blend'),{scene,text},fake_user=True,compress=True)
bpy.ops.object.select_all(action='DESELECT')
for obj in parts: obj.select_set(True)
bpy.context.view_layer.objects.active=parts[0]
for area in bpy.context.screen.areas:
    if area.type=='VIEW_3D':
        area.spaces.active.region_3d.view_perspective='CAMERA'
        area.spaces.active.shading.type='MATERIAL'
print(json.dumps({'objects':len(parts),'vertices':len(positions)//3,'triangles':len(triangles)//3,
    'blend':str(ROOT/'art/handcuffs.blend'),'preview':scene.render.filepath}))
