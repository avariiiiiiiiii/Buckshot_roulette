"""Jammer based on art/references/original/Jammer.webp: flat screened player. Units: metres."""
import bpy, bmesh, json, math, random
from pathlib import Path
from mathutils import Vector
ROOT=Path(r'C:\Users\admin\Documents\GitHub\Mods\Buckshot_roulette')
OUT=ROOT/'assets/models'
scene=bpy.data.scenes.get('Jammer_Workshop') or bpy.data.scenes.new('Jammer_Workshop')
bpy.context.window.scene=scene
for old in list(bpy.data.collections):
    if old.name=='Jammer_Asset':
        for obj in list(old.objects): bpy.data.objects.remove(obj,do_unlink=True)
        bpy.data.collections.remove(old)
asset=bpy.data.collections.new('Jammer_Asset'); scene.collection.children.link(asset)
parts=[]
rng=random.Random(318)
image=bpy.data.images.new('jammer_atlas',width=64,height=64,alpha=False)
pixels=[]
for y in range(64):
    for x in range(64):
        variation=random.Random((x//2)*197+(y//2)*937).uniform(-0.012,0.012)+rng.uniform(-0.01,0.01)
        if y>=32 and x<32: base=(0.25,0.26,0.24)
        elif y>=32: base=(0.64,0.60,0.49)
        elif y>=16 and x<32: base=(0.10,0.105,0.10)
        elif y>=16: base=(0.022,0.024,0.021)
        elif x<32: base=(0.075,0.008,0.008)
        else: base=(0.65,0.30,0.035)
        pixels.extend([max(0,min(1,v+variation)) for v in base]+[1])
for i in range(85):
    x=rng.randrange(1,28); y=rng.randrange(33,63)
    for dx in range(rng.randrange(1,4)):
        at=(y*64+x+dx)*4; pixels[at:at+3]=[0.38,0.38,0.34] if i%3 else [0.14,0.15,0.13]
for i in range(0):
    x=rng.randrange(1,30); y=rng.randrange(1,15)
    at=(y*64+x)*4; pixels[at:at+3]=[0.26,0.035,0.025] if i%4 else [0.59,0.61,0.63]
image.pixels.foreach_set(pixels)
image.filepath_raw=str(OUT/'jammer.png'); image.file_format='PNG'; image.save(); image.pack()
material=bpy.data.materials.new('JammerAtlas'); material.use_nodes=True
shader=material.node_tree.nodes.get('Principled BSDF'); shader.inputs['Roughness'].default_value=0.65
texture=material.node_tree.nodes.new('ShaderNodeTexImage'); texture.image=image; texture.interpolation='Closest'
material.node_tree.links.new(texture.outputs['Color'],shader.inputs['Base Color'])
led_material=material.copy(); led_material.name='JammerChargeIndicators'
led_shader=led_material.node_tree.nodes.get('Principled BSDF')
led_shader.inputs['Emission Color'].default_value=(0.7,0.18,0.005,1)
led_shader.inputs['Emission Strength'].default_value=0.3
zones={'steel':(1,33,30,30),'edge':(33,33,30,30),'shadow':(1,17,30,14),
       'dark':(33,17,30,14),'red':(1,1,30,14),'amber':(33,1,30,14)}

def finish(obj,zone='steel'):
    if obj.name not in asset.objects:
        for collection in list(obj.users_collection): collection.objects.unlink(obj)
        asset.objects.link(obj)
    obj.data.materials.clear(); obj.data.materials.append(led_material if zone=='amber' else material)
    clean=bmesh.new(); clean.from_mesh(obj.data)
    bmesh.ops.remove_doubles(clean,verts=list(clean.verts),dist=0.0000001)
    bmesh.ops.dissolve_degenerate(clean,edges=list(clean.edges),dist=0.0000001)
    bmesh.ops.recalc_face_normals(clean,faces=list(clean.faces))
    clean.to_mesh(obj.data); clean.free()
    obj.data.update()
    for old_uv in list(obj.data.uv_layers): obj.data.uv_layers.remove(old_uv)
    uv=obj.data.uv_layers.new(name='Atlas')
    uv.active_render=True
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
    # Printed marks need only their visible face, not a closed six-sided box.
    if name.startswith(('CoolingVent_', 'SoundSymbol', 'ScrewSlot_', 'ButtonStripe', 'ChargePanelDivider')):
        axis=0 if name.startswith(('CoolingVent_', 'SoundSymbol')) else (2 if name.startswith('ChargePanelDivider') else 1)
        sign=(1 if location[0]>0 else -1) if axis==0 else (1 if axis==2 or name=='ButtonStripe' else -1)
        other=[i for i in range(3) if i!=axis]
        verts=[]
        for u,v in [(-1,-1),(1,-1),(1,1),(-1,1)]:
            p=list(location);p[axis]+=sign*size[axis]/2;p[other[0]]+=u*size[other[0]]/2;p[other[1]]+=v*size[other[1]]/2
            verts.append(p)
        face=(0,1,2,3)
        if (Vector(verts[1])-Vector(verts[0])).cross(Vector(verts[2])-Vector(verts[0]))[axis]*sign<0:
            face=tuple(reversed(face))
        return mesh(name,verts,[face],zone)
    # Preserve the long edge highlights; omit microscopic bevels on rail ends.
    if name.startswith(('GrilleRail_', 'GripRib_')):
        hx,hy,hz=[v/2 for v in size]; b=min(bevel,hy*0.95,hz*0.95)
        profile=[(-hy+b,-hz),(hy-b,-hz),(hy,-hz+b),(hy,hz-b),
                 (hy-b,hz),(-hy+b,hz),(-hy,hz-b),(-hy,-hz+b)]
        verts=[(x,y,z) for x in [-hx,hx] for y,z in profile]
        faces=[tuple(reversed(range(8))),tuple(range(8,16))]
        faces.extend((i,(i+1)%8,(i+1)%8+8,i+8) for i in range(8))
        obj=mesh(name,verts,faces,zone);obj.location=location
        return obj
    if name.startswith(('SideFastener_', 'ChargeBezel_')): bevel=0
    bpy.ops.mesh.primitive_cube_add(size=1,location=location)
    obj=bpy.context.object; obj.name=name; obj.dimensions=size
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    if bevel:
        mod=obj.modifiers.new('Cut corners','BEVEL'); mod.width=bevel; mod.segments=1
        bpy.ops.object.modifier_apply(modifier=mod.name)
    return finish(obj,zone)

def pin(name,location,radius,depth,zone='shadow'):
    segments=8 if name.startswith('FrontScrew_') else 10
    bpy.ops.mesh.primitive_cylinder_add(vertices=segments,radius=radius,depth=depth,location=location,rotation=(math.pi/2,0,0))
    obj=bpy.context.object; obj.name=name
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    return finish(obj,zone)


# Reference silhouette: a thin rectangular dark instrument, front facing -Y.
# The original photograph is small: preserve its visible large screen and bottom controls.
box('RearShell',(0,0.002,0),(0.134,0.025,0.166),'shadow',0.004)
box('CaseSeam',(0,-0.008,0),(0.136,0.002,0.167),'dark',0.003)
box('FrontShell',(0,-0.014,0),(0.134,0.010,0.166),'shadow',0.003)
box('ScreenSurround',(0,-0.020,0.021),(0.117,0.004,0.116),'steel',0.002)
box('ScreenGasket',(0,-0.0225,0.021),(0.110,0.002,0.109),'dark',0.001)
box('DarkMaroonScreen',(0,-0.024,0.021),(0.103,0.001,0.102),'red',0)

def print_rect(name,x,z,width,height,zone='edge',depth=-0.025):
    return mesh(name,[(x-width/2,depth,z-height/2),(x+width/2,depth,z-height/2),
                      (x+width/2,depth,z+height/2),(x-width/2,depth,z+height/2)],
                [(0,1,2,3)],zone)

# Three charge windows remain individually driven by the existing gameplay.
for i,x in enumerate([-0.034,0,0.034]):
    box('ChargeBezel_'+str(i+1),(x,-0.0205,-0.041),(0.011,0.002,0.005),'dark',0)
    box('Charge_'+str(i+1),(x,-0.022,-0.041),(0.007,0.001,0.002),'amber',0)

# Raised rectangular left / confirm / right keys, as on the original player.
for i,x in enumerate([-0.036,0,0.036]):
    box('KeyRecess_'+str(i),(x,-0.020,-0.053),(0.025,0.003,0.014),'dark',0.001)
    box('Key_'+str(i),(x,-0.022,-0.053),(0.020,0.003,0.010),'steel',0.0007)
for side in [-1,1]:
    x=side*0.036
    mesh('Arrow_'+str(side),[(x+side*0.003,-0.024,-0.053),
         (x-side*0.002,-0.024,-0.050),(x-side*0.002,-0.024,-0.056)],
         [(0,1,2) if side==1 else (0,2,1)],'edge')
mesh('Confirm', [(-0.004,-0.024,-0.053),(-0.001,-0.024,-0.056),
    (0.005,-0.024,-0.050),(0.004,-0.024,-0.049),(-0.001,-0.024,-0.054),
    (-0.003,-0.024,-0.052)],[(0,1,2,3,4,5)],'edge')

# Worn cream pixel lettering beneath the keys, matching the reference's pale label.
letters={'J':['00111','00010','00010','00010','10010','10010','01100'],
         'A':['01110','10001','10001','11111','10001','10001','10001'],
         'M':['10001','11011','10101','10101','10001','10001','10001'],
         'E':['11111','10000','10000','11110','10000','10000','11111'],
         'R':['11110','10001','10001','11110','10100','10010','10001']}
pixel=0.0017
for letter_index,letter in enumerate('JAMMER'):
    for row,line in enumerate(letters[letter]):
        # Combine neighbouring pixels into one rectangle to keep the label inexpensive.
        col=0
        while col<5:
            if line[col]=='0': col+=1; continue
            stop=col+1
            while stop<5 and line[stop]=='1': stop+=1
            x=-0.0306+(letter_index*6+(col+stop-1)/2)*pixel
            print_rect('Label_'+str(letter_index)+'_'+str(row)+'_'+str(col),
                       x,-0.065-row*pixel,(stop-col)*pixel,pixel*0.87,depth=-0.0192)
            col=stop

# Subtle case hardware on the rear and a small side socket; no grip or horn.
box('BatteryDoor',(0,0.0155,-0.014),(0.105,0.002,0.116),'dark',0.002)
box('BatteryDoorInset',(0,0.0167,-0.014),(0.100,0.001,0.111),'shadow',0.001)
for x in [-0.052,0.052]:
    for z in [-0.069,0.069]:
        pin('CaseScrew_'+str(x)+'_'+str(z),(x,0.016,z),0.002,0.001,'steel')
box('SideSocket',(0.0675,0,0.045),(0.001,0.010,0.017),'dark',0)
box('SideKey',(0.068,0,-0.003),(0.003,0.012,0.019),'steel',0.001)

# Static game mesh and separate indicator material; all parts stay editable in Blender.
bpy.context.view_layer.update()
for obj in parts:
    if obj.name.startswith(('Label_', 'Arrow_', 'Confirm')):
        # Recalculating a disconnected open face has no unambiguous outside.
        # Orient the printed face explicitly so Unity backface culling keeps it visible.
        for face in obj.data.polygons:
            if face.normal.y > 0: face.flip()
        obj.data.update()
        assert all(face.normal.y < -0.99 for face in obj.data.polygons), obj.name + ': reversed front face'
positions=[]; normals=[]; uvs=[]; keymap={}; triangles={'body':[],'indicators':[]}
for obj in parts:
    data=obj.data; data.calc_loop_triangles(); uv=data.uv_layers.active.data
    bucket=triangles['indicators' if obj.get('atlas_zone')=='amber' else 'body']
    for tri in data.loop_triangles:
        ids=[]
        for li in tri.loops:
            vi=data.loops[li].vertex_index
            point=obj.matrix_world@data.vertices[vi].co
            normal=(obj.matrix_world.to_3x3().inverted().transposed()@data.corner_normals[li].vector).normalized()
            p=(-point.x,point.z,-point.y); n=(-normal.x,normal.z,-normal.y); t=uv[li].uv
            key=tuple(round(v,6) for v in (*p,*n,*t))
            if key not in keymap:
                keymap[key]=len(positions)//3; positions.extend(p); normals.extend(n); uvs.extend(t)
            ids.append(keymap[key])
        a,b,c=[Vector(positions[j*3:j*3+3]) for j in ids]
        n=Vector(normals[ids[0]*3:ids[0]*3+3])
        if (b-a).cross(c-a).dot(n)<0: ids[1],ids[2]=ids[2],ids[1]
        bucket.extend(ids)
submeshes=[]
for name,indices in triangles.items():
    sub={'material':'jammer_'+name,'color':[1,1,1],'metallic':0.0,'smoothness':0.35,'texture':'jammer.png','triangles':indices}
    if name=='indicators': sub['emission']=[0.18,0.06,0.003]
    submeshes.append(sub)
result={'positions':[round(v,6) for v in positions],'normals':[round(v,5) for v in normals],
        'uvs':[round(v,6) for v in uvs],'submeshes':submeshes,
        'markers':{'emitter':[0,0.021,0.025],'grip':[0,-0.025,0],'button':[0,-0.053,0.024]}}
(OUT/'jammer.json').write_text(json.dumps(result,separators=(',',':')),encoding='utf-8')

# A dedicated studio scene preserves the handcuffs workshop untouched.
scene.world=bpy.data.worlds.new('Jammer Studio'); scene.world.use_nodes=True
scene.world.node_tree.nodes['Background'].inputs['Color'].default_value=(0.14,0.16,0.19,1)
scene.world.node_tree.nodes['Background'].inputs['Strength'].default_value=0.45
cam=scene.camera
if cam is None:
    cam=bpy.data.objects.new('Jammer Camera',bpy.data.cameras.new('Jammer Camera'))
    scene.collection.objects.link(cam); scene.camera=cam
cam.data.type='ORTHO'; cam.data.ortho_scale=0.23
cam.location=(0.20,-0.50,0.20); target=Vector((0,-0.008,0.006))
cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler()
for name,location,power,size in [('Jammer Key',(-0.3,-0.4,0.7),14,0.7),('Jammer Fill',(0.4,0.1,0.4),8,0.6)]:
    lamp=scene.objects.get(name)
    if lamp is None:
        lamp=bpy.data.objects.new(name,bpy.data.lights.new(name,'AREA')); scene.collection.objects.link(lamp)
    lamp.location=location; lamp.data.energy=power; lamp.data.size=size
    lamp.rotation_euler=(target-lamp.location).to_track_quat('-Z','Y').to_euler()
scene.view_settings.view_transform='Standard'; scene.view_settings.look='None'
scene.render.engine='CYCLES'; scene.cycles.samples=32; scene.cycles.device='CPU'
scene.render.resolution_x=1000; scene.render.resolution_y=1000; scene.render.resolution_percentage=100
(ROOT/'art/previews').mkdir(exist_ok=True)
scene.render.filepath=str(ROOT/'art/previews/jammer.png')
bpy.ops.render.render(write_still=True)
# Save a rear view too, so button and grip details can be inspected without opening Blender.
cam.location=(0.20,0.50,0.20); cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler()
scene.render.filepath=str(ROOT/'art/previews/jammer_rear.png'); bpy.ops.render.render(write_still=True)
cam.location=(0.20,-0.50,0.20); cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler()
scene.render.filepath=str(ROOT/'art/previews/jammer.png')
text=bpy.data.texts.get('jammer_pipeline.py') or bpy.data.texts.new('jammer_pipeline.py')
text.clear(); text.write((ROOT/'art/jammer_pipeline.py').read_text(encoding='utf-8'))
scene['asset_notes']='Reference: art/references/original/Jammer.webp. Flat dark player, large maroon screen, three bottom keys and cream label. Three small charge lamps are the mod gameplay addition. Pixel atlas 64x64.'
bpy.ops.object.select_all(action='DESELECT')
for obj in parts: obj.select_set(True)
bpy.context.view_layer.objects.active=parts[0]
for area in bpy.context.screen.areas:
    if area.type=='VIEW_3D':
        area.spaces.active.region_3d.view_perspective='CAMERA'
        area.spaces.active.shading.type='MATERIAL'
bpy.data.libraries.write(str(ROOT/'art/jammer.blend'),{scene,text},fake_user=True,compress=True)
print(json.dumps({'objects':len(parts),'triangles':sum(len(t) for t in triangles.values())//3,'vertices':len(positions)//3,'blend':str(ROOT/'art/jammer.blend'),'preview':str(ROOT/'art/previews/jammer.png')}))
