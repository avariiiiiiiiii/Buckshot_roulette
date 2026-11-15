"""Low-poly adrenaline injector from the supplied Adrenaline.webp, via Blender MCP.

Metres. Long axis is Blender Y. Editable components, pixel atlas and Unity JSON.
The small photograph resolves the silhouette and plates, not precise inner mechanics.
"""
import bpy, bmesh, json, math, random
from pathlib import Path
from mathutils import Vector

ROOT=Path(r'C:\Users\admin\Documents\GitHub\Mods\Buckshot_roulette')
OUT=ROOT/'assets/models'
scene=bpy.data.scenes.get('Adrenaline_Workshop') or bpy.data.scenes.new('Adrenaline_Workshop')
bpy.context.window.scene=scene
old=bpy.data.collections.get('Adrenaline_Asset')
if old:
    for obj in list(old.objects):bpy.data.objects.remove(obj,do_unlink=True)
    bpy.data.collections.remove(old)
asset=bpy.data.collections.new('Adrenaline_Asset');scene.collection.children.link(asset)
parts=[]

# Worn, desaturated metal. No bright medical colours absent from the reference.
palette={'steel':(.25,.27,.25),'edge':(.31,.33,.30),'body':(.12,.13,.11),
         'black':(.025,.026,.022),'red':(.20,.045,.035),'label':(.55,.50,.38)}
zones={name:(i%3*21,i//3*32,21,32) for i,name in enumerate(palette)}
rng=random.Random(441)
pixels=[0.0]*(64*64*4)
for name,(bx,by,w,h) in zones.items():
    base=palette[name]
    for y in range(by,min(by+h,64)):
        for x in range(bx,min(bx+w,64)):
            coarse=random.Random((x//2)*173+(y//2)*733).uniform(-.018,.018)
            d=coarse+rng.uniform(-.006,.006)
            at=(y*64+x)*4;pixels[at:at+4]=[max(0,min(1,c+d)) for c in base]+[1]
    if name in {'steel','edge','body'}:
        for i in range(26):
            x=bx+rng.randrange(2,w-3);y=by+rng.randrange(2,h-3)
            for dy in range(rng.randrange(1,4)):
                at=((y+dy)*64+x)*4
                pixels[at:at+3]=[c*.65 if i%3 else min(1,c*1.25) for c in base]
# Last atlas column is padding, not a separate colour.
for y in range(64):pixels[(y*64+63)*4:(y*64+64)*4]=pixels[(y*64+62)*4:(y*64+63)*4]
image=bpy.data.images.new('Adrenaline worn metal atlas',width=64,height=64,alpha=False)
image.pixels.foreach_set(pixels);image.filepath_raw=str(OUT/'adrenaline.png');image.file_format='PNG';image.save();image.pack()
material=bpy.data.materials.new('AdrenalineAtlas');material.use_nodes=True
shader=material.node_tree.nodes.get('Principled BSDF')
shader.inputs['Roughness'].default_value=.66;shader.inputs['Metallic'].default_value=.25
tex=material.node_tree.nodes.new('ShaderNodeTexImage');tex.image=image;tex.interpolation='Closest'
material.node_tree.links.new(tex.outputs['Color'],shader.inputs['Base Color'])

def finish(obj,zone):
    for collection in list(obj.users_collection):collection.objects.unlink(obj)
    asset.objects.link(obj)
    clean=bmesh.new();clean.from_mesh(obj.data)
    bmesh.ops.remove_doubles(clean,verts=list(clean.verts),dist=1e-7)
    bmesh.ops.dissolve_degenerate(clean,edges=list(clean.edges),dist=1e-7)
    bmesh.ops.recalc_face_normals(clean,faces=list(clean.faces))
    clean.to_mesh(obj.data);clean.free();obj.data.update()
    obj.data.materials.clear();obj.data.materials.append(material)
    for uv in list(obj.data.uv_layers):obj.data.uv_layers.remove(uv)
    uv=obj.data.uv_layers.new(name='Atlas');uv.active_render=True
    for poly in obj.data.polygons:
        n=poly.normal
        chosen='edge' if zone=='steel' and n.z>.6 else zone
        bx,by,w,h=zones[chosen]
        ax,ay=(0,1) if abs(n.z)>.6 else ((0,2) if abs(n.y)>.6 else (1,2))
        for li in poly.loop_indices:
            p=obj.data.vertices[obj.data.loops[li].vertex_index].co
            uv.data[li].uv=((bx+2+(p[ax]*300+3)%(w-4))/64,
                            (by+2+(p[ay]*300+7)%(h-4))/64)
        poly.use_smooth=False
    obj['atlas_zone']=zone;parts.append(obj)
    return obj

def raw(name,verts,faces,zone):
    data=bpy.data.meshes.new(name);data.from_pydata(verts,[],faces);data.update()
    obj=bpy.data.objects.new(name,data);asset.objects.link(obj)
    return finish(obj,zone)

def box(name,loc,size,zone,bevel=0):
    bpy.ops.mesh.primitive_cube_add(size=1,location=loc)
    obj=bpy.context.object;obj.name=name;obj.dimensions=size
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    if bevel:
        mod=obj.modifiers.new('Machined corners','BEVEL');mod.width=bevel;mod.segments=1
        bpy.ops.object.modifier_apply(modifier=mod.name)
    return finish(obj,zone)

def plan_prism(name,outline,bottom,top,zone):
    # Extrude the XY outline; planar top and bottom remain simple polygons.
    verts=[(x,y,z) for z in [bottom,top] for x,y in outline]
    count=len(outline)
    faces=[tuple(reversed(range(count))),tuple(range(count,count*2))]
    faces.extend((i,(i+1)%count,(i+1)%count+count,i+count) for i in range(count))
    return raw(name,verts,faces,zone)

def frame(name,outer,inner,bottom,top,zone):
    # Eight-corner rectangular ring with a real recess through its middle.
    count=len(outer);assert count==len(inner)
    verts=[(x,y,z) for outline,z in [(outer,bottom),(outer,top),(inner,top),(inner,bottom)] for x,y in outline]
    faces=[]
    for i in range(count):
        j=(i+1)%count
        faces.extend([(i,j,count+j,count+i),(count+i,count+j,2*count+j,2*count+i),
                      (2*count+i,2*count+j,3*count+j,3*count+i),(3*count+i,3*count+j,j,i)])
    return raw(name,verts,faces,zone)

def octagon(cx,cy,w,h,cut):
    x=w/2;y=h/2;c=cut
    return [(cx-x+c,cy-y),(cx+x-c,cy-y),(cx+x,cy-y+c),(cx+x,cy+y-c),
            (cx+x-c,cy+y),(cx-x+c,cy+y),(cx-x,cy+y-c),(cx-x,cy-y+c)]

# Long black undercarriage and tapered steel spine.
plan_prism('InjectorUndercarriage',octagon(0,-.004,.039,.173,.006),.002,.014,'body')
plan_prism('MainSteelSpine',[( -.015,-.075),(.015,-.075),(.019,-.057),(.019,.045),
    (.014,.057),(-.014,.057),(-.019,.045),(-.019,-.057)],.012,.023,'steel')

# Rear actuator: a long narrow red insert inside a raised dark rectangular frame.
frame('RearActuatorFrame',octagon(0,.079,.034,.064,.006),
      octagon(0,.079,.022,.051,.004),.009,.026,'body')
plan_prism('RecessedRearActuator',octagon(0,.078,.020,.048,.004),.012,.019,'red')
plan_prism('ActuatorTopPlate',octagon(0,.079,.012,.037,.002),.019,.020,'body')
for x in [-.012,.012]:
    box('RearFrameRail_'+str(x),(x,.075,.027),(.003,.040,.002),'steel')

# Heavy middle clamp and its two angular side ears, visible in the photograph.
box('CentralClamp',(0,.034,.022),(.045,.019,.018),'body',.002)
box('ClampTop',(0,.035,.032),(.031,.013,.003),'steel',.001)
box('ClampSlot',(0,.034,.034),(.017,.004,.0006),'black')
for side in [-1,1]:
    plan_prism('ClampEar_'+str(side),[(side*.018,.025),(side*.026,.018),
        (side*.026,.036),(side*.020,.044)],.008,.019,'body')
    box('ClampEarInset_'+str(side),(side*.023,.029,.020),(.003,.009,.001),'black')

# Repeated slots/ribs along the grey centre rather than a plain syringe barrel.
box('CentralBed',(0,-.019,.024),(.028,.079,.003),'body',.001)
plan_prism('CentreRibbedPlate',octagon(0,-.019,.021,.078,.003),.025,.029,'steel')
for i in range(7):
    y=.011-i*.009
    for side in [-1,1]:
        # Narrow open grooves between the top plate and its side guide rails.
        box('RibNotch_'+str(side)+'_'+str(i),(side*.0115,y,.0288),(.004,.0035,.001),'black')
    box('CentreRib_'+str(i),(0,y-.002,.030),(.017,.0015,.0012),'steel')
for side in [-1,1]:
    plan_prism('LongGuideRail_'+str(side),[(side*.014,-.070),(side*.020,-.064),
        (side*.020,.019),(side*.015,.023)],.018,.027,'body')
    box('GuideHighlight_'+str(side),(side*.018,-.021,.0275),(.002,.068,.001),'steel')
    for i in range(4):
        box('SideRecess_'+str(side)+'_'+str(i),(side*.0202,-.007-i*.013,.019),(.0005,.007,.004),'black')

# Forked injector nose, with two angular guards and a shorter central metal pin.
plan_prism('NoseCollar',octagon(0,-.070,.037,.019,.003),.007,.025,'body')
box('NoseTopPlate',(0,-.071,.0255),(.025,.010,.002),'steel',.0005)
for side in [-1,1]:
    plan_prism('TipGuard_'+str(side),[(side*.012,-.075),(side*.019,-.075),
        (side*.016,-.105),(side*.012,-.112),(side*.009,-.107)],.009,.018,'steel')
    box('TipGuardDarkInset_'+str(side),(side*.014,-.090,.0185),(.002,.021,.0007),'body')
plan_prism('CentralInjectorPin',[(-.003,-.076),(.003,-.076),(.002,-.101),(0,-.108),(-.002,-.101)],.011,.014,'edge')

# Underbody structure is deliberately modest: the reference does not show internals.
box('UndersideInset',(0,-.013,.0015),(.025,.108,.002),'black',.0005)
box('UndersidePlate',(0,-.011,.0005),(.019,.085,.001),'body')

# One compact game mesh, using a single pixel texture and a single material.
bpy.context.view_layer.update()
positions=[];normals=[];uvs=[];lookup={};triangles=[]
for obj in parts:
    data=obj.data;data.calc_loop_triangles();uv=data.uv_layers.active.data
    for tri in data.loop_triangles:
        ids=[]
        for li in tri.loops:
            point=obj.matrix_world@data.vertices[data.loops[li].vertex_index].co
            normal=(obj.matrix_world.to_3x3().inverted().transposed()@data.corner_normals[li].vector).normalized()
            p=(-point.x,point.z,-point.y);n=(-normal.x,normal.z,-normal.y);t=uv[li].uv
            key=tuple(round(v,7) for v in (*p,*n,*t))
            if key not in lookup:
                lookup[key]=len(positions)//3;positions.extend(p);normals.extend(n);uvs.extend(t)
            ids.append(lookup[key])
        a,b,c=[Vector(positions[i*3:i*3+3]) for i in ids]
        if (b-a).cross(c-a).dot(Vector(normals[ids[0]*3:ids[0]*3+3]))<0:ids[1],ids[2]=ids[2],ids[1]
        triangles.extend(ids)
markers={'tip':[0,.0125,.108],'grip':[0,.015,.020],'actuator':[0,.020,-.079]}
result={'name':'adrenaline','positions':[round(v,7) for v in positions],
    'normals':[round(v,6) for v in normals],'uvs':[round(v,6) for v in uvs],
    'submeshes':[{'material':'adrenaline_atlas','color':[1,1,1],'metallic':.25,'smoothness':.34,
        'texture':'adrenaline.png','triangles':triangles}],'markers':markers}
(OUT/'adrenaline.json').write_text(json.dumps(result,separators=(',',':')),encoding='utf-8')

scene.world=bpy.data.worlds.new('Adrenaline Studio');scene.world.use_nodes=True
scene.world.node_tree.nodes['Background'].inputs['Color'].default_value=(.13,.14,.15,1)
scene.world.node_tree.nodes['Background'].inputs['Strength'].default_value=.45
cam=scene.camera
if cam is None:
    cam=bpy.data.objects.new('Adrenaline Camera',bpy.data.cameras.new('Adrenaline Camera'))
    scene.collection.objects.link(cam);scene.camera=cam
cam.data.type='ORTHO';cam.data.ortho_scale=.265
target=Vector((0,0,.014));front=Vector((.24,-.30,.40))
cam.location=front;cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler()
for name,loc,power,size in [('Key',(-.25,-.3,.6),12,.6),('Fill',(.4,.3,.4),7,.6)]:
    light=scene.objects.get('Adrenaline '+name)
    if light is None:
        light=bpy.data.objects.new('Adrenaline '+name,bpy.data.lights.new(name,'AREA'));scene.collection.objects.link(light)
    light.location=loc;light.data.energy=power;light.data.size=size
    light.rotation_euler=(target-light.location).to_track_quat('-Z','Y').to_euler()
scene.view_settings.view_transform='Standard';scene.view_settings.look='None'
scene.render.engine='CYCLES';scene.cycles.samples=32;scene.cycles.device='CPU'
scene.render.resolution_x=1200;scene.render.resolution_y=900;scene.render.resolution_percentage=100
scene.render.filepath=str(ROOT/'art/previews/adrenaline.png');bpy.ops.render.render(write_still=True)
cam.location=(.20,.26,-.35);cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler()
scene.render.filepath=str(ROOT/'art/previews/adrenaline_underside.png');bpy.ops.render.render(write_still=True)
cam.location=front;cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler()
scene.render.filepath=str(ROOT/'art/previews/adrenaline.png')
text=bpy.data.texts.get('adrenaline_pipeline.py') or bpy.data.texts.new('adrenaline_pipeline.py')
text.clear();text.write((ROOT/'art/adrenaline_pipeline.py').read_text(encoding='utf-8'))
scene['asset_notes']='Based on user Adrenaline.webp: elongated dark mechanical injector, recessed rear actuator, ribbed grey central plate, forked metal nose. Small internal details approximated. Gameplay not implemented.'
bpy.ops.object.select_all(action='DESELECT')
for obj in parts:obj.select_set(True)
bpy.context.view_layer.objects.active=parts[0]
for area in bpy.context.screen.areas:
    if area.type=='VIEW_3D':
        area.spaces.active.region_3d.view_perspective='CAMERA';area.spaces.active.shading.type='MATERIAL'
bpy.data.libraries.write(str(ROOT/'art/adrenaline.blend'),{scene,text},fake_user=True,compress=True)
print(json.dumps({'objects':len(parts),'triangles':len(triangles)//3,'vertices':len(positions)//3,
    'source':str(ROOT/'art/adrenaline.blend'),'preview':str(ROOT/'art/previews/adrenaline.png')}))
