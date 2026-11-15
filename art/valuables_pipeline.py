"""Reference-based beer, cigarette pack and medicine. Run through Blender MCP.

Metres. Replaces only these three groups in models.blend; phone and legacy source
geometry are preserved. Reference photographs are never used as painted textures.
"""
import bpy, bmesh, json, math, random
from pathlib import Path
from mathutils import Vector

ROOT=Path(r'C:\Users\admin\Documents\GitHub\Mods\Buckshot_roulette')
OUT=ROOT/'assets/models'
for old in list(bpy.data.scenes):
    if old.get('buckshot_workshop')=='valuables':
        for obj in list(old.objects):
            if len(obj.users_scene)==1: bpy.data.objects.remove(obj,do_unlink=True)
        bpy.data.scenes.remove(old)
with bpy.data.libraries.load(str(ROOT/'art/models.blend'),link=False) as (src,dst):
    dst.scenes=[next((name for name in src.scenes if name.startswith('Valuables_Workshop')),src.scenes[0])]
scene=dst.scenes[0];scene.name='Valuables_Workshop'
scene['buckshot_workshop']='valuables';bpy.context.window.scene=scene
old_beer={'body','tab','tab_hole','opening','star_a','star_b'}
for obj in list(scene.objects):
    name=obj.name.split('.')[0]
    if name.startswith(('md_','cg_','beer_')) or name in old_beer:
        bpy.data.objects.remove(obj,do_unlink=True)
    elif name.startswith(('d_','dg_')):
        obj.hide_render=True;obj.hide_set(True)

# Small bitmap lettering keeps print in the texture, not hundreds of mesh faces.
FONT={
'A':['01110','10001','10001','11111','10001','10001','10001'],
'B':['11110','10001','10001','11110','10001','10001','11110'],
'C':['01111','10000','10000','10000','10000','10000','01111'],
'D':['11110','10001','10001','10001','10001','10001','11110'],
'E':['11111','10000','10000','11110','10000','10000','11111'],
'F':['11111','10000','10000','11110','10000','10000','10000'],
'G':['01111','10000','10000','10111','10001','10001','01111'],
'H':['10001','10001','10001','11111','10001','10001','10001'],
'I':['1','1','1','1','1','1','1'],
'L':['10000','10000','10000','10000','10000','10000','11111'],
'M':['10001','11011','10101','10101','10001','10001','10001'],
'N':['10001','11001','10101','10011','10001','10001','10001'],
'O':['01110','10001','10001','10001','10001','10001','01110'],
'P':['11110','10001','10001','11110','10000','10000','10000'],
'R':['11110','10001','10001','11110','10100','10010','10001'],
'S':['01111','10000','10000','01110','00001','00001','11110'],
'T':['11111','00100','00100','00100','00100','00100','00100'],
'U':['10001','10001','10001','10001','10001','10001','01110'],
'V':['10001','10001','10001','10001','10001','01010','00100'],
'X':['10001','10001','01010','00100','01010','10001','10001'],
'Y':['10001','10001','01010','00100','00100','00100','00100'],
'0':['01110','10001','10011','10101','11001','10001','01110'],
'1':['010','110','010','010','010','010','111'],
'2':['01110','10001','00001','00010','00100','01000','11111'],
'3':['11110','00001','00001','01110','00001','00001','11110'],
'5':['11111','10000','10000','11110','00001','00001','11110'],
'6':['01110','10000','10000','11110','10001','10001','01110'],
'8':['01110','10001','10001','01110','10001','10001','01110'],
'9':['01110','10001','10001','01111','00001','00001','01110'],
' ':['000']*7,'-':['00000','00000','00000','11111','00000','00000','00000'],
'.':['0','0','0','0','0','1','1'],
}

class Paint:
    def __init__(self,name,base,seed):
        self.name=name;self.w=128;self.h=128;self.rng=random.Random(seed)
        self.p=[list(base) for _ in range(self.w*self.h)]
    def dot(self,x,y,c):
        if 0<=x<self.w and 0<=y<self.h:self.p[y*self.w+x]=list(c)
    def rect(self,x,y,w,h,c):
        for yy in range(y,y+h):
            for xx in range(x,x+w): self.dot(xx,yy,c)
    def line(self,x0,y0,x1,y1,c,width=1):
        steps=max(abs(x1-x0),abs(y1-y0),1)
        for i in range(steps+1):
            x=round(x0+(x1-x0)*i/steps);y=round(y0+(y1-y0)*i/steps)
            self.rect(x,y,width,width,c)
    def text(self,word,x,y,c,scale=1):
        for letter in word:
            glyph=FONT[letter]
            for row,line in enumerate(glyph):
                for col,on in enumerate(line):
                    if on=='1':self.rect(x+col*scale,y+(6-row)*scale,scale,scale,c)
            x+=(len(glyph[0])+1)*scale
    def grain(self,amount):
        for i,c in enumerate(self.p):
            # Coarse blocks with finer paper grain, kept subtle for game lighting.
            d=self.rng.uniform(-amount,amount)
            self.p[i]=[min(1,max(0,v+d)) for v in c]
    def material(self,metallic=0,smoothness=.22):
        image=bpy.data.images.new(self.name+'_print',width=128,height=128,alpha=False)
        image.pixels.foreach_set([v for c in self.p for v in (*c,1)])
        image.filepath_raw=str(OUT/(self.name+'.png'));image.file_format='PNG';image.save();image.pack()
        mat=bpy.data.materials.new(self.name+'_print');mat.use_nodes=True
        bsdf=mat.node_tree.nodes.get('Principled BSDF')
        bsdf.inputs['Metallic'].default_value=metallic;bsdf.inputs['Roughness'].default_value=1-smoothness
        tex=mat.node_tree.nodes.new('ShaderNodeTexImage');tex.image=image;tex.interpolation='Closest'
        mat.node_tree.links.new(tex.outputs['Color'],bsdf.inputs['Base Color'])
        mat['export_texture']=self.name+'.png';mat['export_color']=[1,1,1]
        mat['export_metallic']=metallic;mat['export_smoothness']=smoothness
        return mat

def solid(name,color,metallic=0,smoothness=.25):
    mat=bpy.data.materials.new(name);mat.use_nodes=True;mat.diffuse_color=(*color,1)
    shader=mat.node_tree.nodes.get('Principled BSDF');shader.inputs['Base Color'].default_value=(*color,1)
    shader.inputs['Metallic'].default_value=metallic;shader.inputs['Roughness'].default_value=1-smoothness
    mat['export_color']=list(color);mat['export_metallic']=metallic;mat['export_smoothness']=smoothness
    return mat

ink=(.055,.038,.033);cream=(.68,.60,.43);red=(.22,.035,.035)
# Cream pharmaceutical carton: dark print, an inset rule and curved ornament seen
# on the reference. Generic wording replaces lettering unreadable in the photo.
p=Paint('expired_medicine',cream,104)
p.rect(6,6,94,116,(.76,.68,.51));p.rect(9,9,88,110,ink);p.rect(10,10,86,108,(.76,.68,.51))
p.text('EXPIRED',14,104,ink,2);p.text('MEDICINE',14,87,ink,2)
p.line(13,82,92,82,red,2);p.text('TABLETS',14,71,red)
p.text('ORAL USE',14,59,ink);p.text('10 TABLETS',14,48,ink)
for i in range(3):p.rect(14,37-i*4,42+i*7,1,(.24,.18,.14))
for i in range(71):
    a=math.radians(125+i*2.1)
    x=round(111+35*math.cos(a));y=round(58+30*math.sin(a))
    if x<94:p.dot(x,y,red)
    x=round(111+34*math.cos(a));y=round(58+29*math.sin(a))
    if x<94:p.dot(x,y,red)
p.rect(13,13,80,13,red);p.text('EXPIRED',17,16,cream)
# A separate narrow side strip and darker cardboard fold area.
p.rect(104,0,24,128,(.53,.45,.33));p.rect(109,7,14,114,(.66,.57,.42))
for i in range(9):p.rect(112,18+i*9,8,1,ink)
p.grain(.025);med_mat=p.material()

# The beer reference is a dark red/cream printed can with a silver opened lid.
p=Paint('beer_can',(.055,.047,.040),207)
p.rect(16,0,31,128,(.20,.035,.035));p.rect(18,0,4,128,(.35,.08,.065))
p.rect(44,0,3,128,(.56,.44,.29));p.rect(84,0,2,128,(.44,.34,.23))
p.rect(0,5,128,2,cream);p.rect(0,119,128,2,cream)
p.text('BEER',49,68,cream,2);p.line(49,62,92,62,red,3)
p.text('LAGER',52,49,cream);p.text('500 ML',51,20,(.47,.40,.28))
for y in [34,37,40]:p.rect(51,y,34-(y%3)*3,1,(.32,.28,.20))
# Small worn patches on the printed label, avoiding large invented emblems.
for i in range(100):
    x=p.rng.randrange(128);y=p.rng.randrange(128)
    if x<45 or y<15:p.rect(x,y,p.rng.randrange(1,3),1,(.24,.22,.18))
p.grain(.018);beer_mat=p.material(.18,.34)

# Dark cigarette packet, cream upper panel and muted burgundy rule. The supplied
# 24x32 image cannot resolve a brand; do not invent one or copy the old red pack.
p=Paint('cigarette_pack',(.045,.032,.027),301)
p.rect(6,6,90,116,(.070,.048,.037))
p.rect(8,76,86,43,(.64,.55,.38));p.rect(12,80,78,35,(.73,.65,.48))
p.text('CIGARETTES',16,99,ink)
p.text('FILTER',31,86,red)
p.rect(8,72,86,3,red);p.rect(8,68,86,1,(.48,.39,.26))
p.text('20',42,23,cream,2)
p.line(13,16,91,16,(.35,.28,.18))
p.rect(104,0,24,128,(.045,.030,.024));p.rect(108,76,15,43,(.55,.46,.31))
for i in range(45):
    x=p.rng.choice([7,8,94,95]);y=p.rng.randrange(7,120)
    p.dot(x,y,(.29,.23,.16))
p.grain(.018);cig_mat=p.material()

silver=solid('Beer brushed aluminum',(.49,.50,.46),.78,.45)
recess=solid('Beer opening',(.018,.016,.012),.15,.22)
cardboard=solid('Cardboard folds',(.34,.27,.18))
pack_seam=solid('Packet lid seam',(.024,.017,.014))
groups={'beer_can':[],'cigarette_pack':[],'expired_medicine':[]}

def finish(obj,model,mat):
    for collection in list(obj.users_collection):collection.objects.unlink(obj)
    scene.collection.objects.link(obj)
    obj.data.materials.clear();obj.data.materials.append(mat)
    clean=bmesh.new();clean.from_mesh(obj.data)
    bmesh.ops.remove_doubles(clean,verts=list(clean.verts),dist=1e-7)
    bmesh.ops.dissolve_degenerate(clean,edges=list(clean.edges),dist=1e-7)
    bmesh.ops.recalc_face_normals(clean,faces=list(clean.faces))
    clean.to_mesh(obj.data);clean.free();obj.data.update()
    for uv in list(obj.data.uv_layers):obj.data.uv_layers.remove(uv)
    uv=obj.data.uv_layers.new(name='Print');uv.active_render=True
    for value in uv.data:value.uv=(.5,.5)
    groups[model].append(obj);obj['buckshot_model']=model
    return obj

def box(name,loc,size,model,mat,bevel=.0005,print_faces=False):
    bpy.ops.mesh.primitive_cube_add(size=1,location=loc)
    obj=bpy.context.object;obj.name=name;obj.dimensions=size
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    if bevel:
        mod=obj.modifiers.new('Folded edges','BEVEL');mod.width=bevel;mod.segments=1
        bpy.ops.object.modifier_apply(modifier=mod.name)
    finish(obj,model,mat)
    if print_faces:
        for face in obj.data.polygons:
            for li in face.loop_indices:
                co=obj.data.vertices[obj.data.loops[li].vertex_index].co
                u=co.x/size[0]+.5;v=co.z/size[2]+.5
                if abs(face.normal.y)>.9:
                    # Mirror the back's U so both sides read correctly from outside.
                    if face.normal.y>0:u=1-u
                    obj.data.uv_layers.active.data[li].uv=((6+94*u)/128,(6+116*v)/128)
                elif abs(face.normal.x)>.9:
                    obj.data.uv_layers.active.data[li].uv=((109+14*(co.y/size[1]+.5))/128,(6+116*v)/128)
                else:obj.data.uv_layers.active.data[li].uv=(106/128,64/128)
    return obj

def raw(name,verts,faces,model,mat):
    data=bpy.data.meshes.new(name);data.from_pydata(verts,[],faces);data.update()
    obj=bpy.data.objects.new(name,data);scene.collection.objects.link(obj)
    bm=bmesh.new();bm.from_mesh(data);bmesh.ops.recalc_face_normals(bm,faces=bm.faces);bm.to_mesh(data);bm.free()
    return finish(obj,model,mat)

def lathe(name,profile,model,mat,segments=16):
    verts=[];faces=[]
    for r,z in profile:
        verts.extend((r*math.sin(2*math.pi*i/segments),-r*math.cos(2*math.pi*i/segments),z) for i in range(segments))
    for j in range(len(profile)-1):
        for i in range(segments):faces.append((j*segments+i,j*segments+(i+1)%segments,(j+1)*segments+(i+1)%segments,(j+1)*segments+i))
    if profile[0][0]>0:faces.append(tuple(reversed(range(segments))))
    if profile[-1][0]>0:faces.append(tuple((len(profile)-1)*segments+i for i in range(segments)))
    obj=raw(name,verts,faces,model,mat)
    return obj

# Slightly faceted can, real recessed opening and a raised ring-pull.
body=lathe('beer_body',[(.0265,0),(.0305,.003),(.0335,.011),(.0335,.137),(.029,.149),(.029,.152)],'beer_can',beer_mat)
# Use explicit strip coordinates to put the UV seam at the back and keep U within [0,1].
for face in body.data.polygons:
    ids=[body.data.loops[k].vertex_index%16 for k in face.loop_indices]
    if len(face.vertices)>4:
        for li in face.loop_indices:body.data.uv_layers.active.data[li].uv=(.02,.02)
        continue
    for li in face.loop_indices:
        vi=body.data.loops[li].vertex_index;index=vi%16
        # Front at i=0 corresponds to u=.5; seam at i=8.
        u=((index+8)%16)/16
        if 7 in ids and 8 in ids and index==8:u=1
        body.data.uv_layers.active.data[li].uv=(u,max(0,min(1,(body.data.vertices[vi].co.z-.01)/.13)))

lathe('beer_bottom_rim',[(.027,.001),(.031,.001),(.0318,.003),(.0318,.005),(.030,.006)],'beer_can',silver)
lathe('beer_top_rim',[(.0288,.149),(.031,.150),(.0315,.152),(.0305,.154),(.028,.154),(.027,.152)],'beer_can',silver)
lathe('beer_lid',[(.0288,.152),(.0288,.1528)],'beer_can',silver)
opening=box('beer_opening',(0,-.009,.1531),(.015,.020,.0005),'beer_can',recess,.002)
# Ring pull lies on top of the lid, using eight low-poly sides and four ring surfaces.
verts=[];faces=[]
for i in range(8):
    a=2*math.pi*i/8
    for rx,ry,z in [(.008,.013,.1533),(.008,.013,.1548),(.004,.007,.1548),(.004,.007,.1533)]:
        verts.append((rx*math.cos(a),.005+ry*math.sin(a),z))
for i in range(8):
    for j in range(4):faces.append((i*4+j,((i+1)%8)*4+j,((i+1)%8)*4+(j+1)%4,i*4+(j+1)%4))
raw('beer_pull_tab',verts,faces,'beer_can',silver)
box('beer_tab_rivet',(0,0,.1543),(.005,.005,.0015),'beer_can',silver,0)

# Closed dark packet with a real lid seam, a paper side fold and a thin tax strip.
box('cg_body',(.6,0,.043),(.058,.024,.086),'cigarette_pack',cig_mat,.0007,True)
box('cg_lid',(.6,0,.0775),(.0585,.0245,.018),'cigarette_pack',cig_mat,.0006)
# Map the lid to the same print rows as the upper part of the packet.
lid=groups['cigarette_pack'][-1]
for face in lid.data.polygons:
    for li in face.loop_indices:
        co=lid.data.vertices[lid.data.loops[li].vertex_index].co
        if abs(face.normal.y)>.9:
            u=co.x/.0585+.5
            if face.normal.y>0:u=1-u
            v=(co.z+.0775)/.086
            lid.data.uv_layers.active.data[li].uv=((6+94*u)/128,(6+116*v)/128)
        else:lid.data.uv_layers.active.data[li].uv=(112/128,112/128)
box('cg_lid_seam',(.6,-.0126,.0682),(.057,.0004,.0007),'cigarette_pack',pack_seam,0)
box('cg_paper_fold',(.6294,0,.037),(.0003,.018,.059),'cigarette_pack',pack_seam,0)

# Long, shallow cardboard carton; no bottle, screw cap or cross-shaped geometry.
box('md_body',(.9,0,.065),(.068,.022,.130),'expired_medicine',med_mat,.00055,True)
box('md_top_flap',(.9,0,.1301),(.066,.020,.0006),'expired_medicine',cardboard,.00015)
box('md_fold_left',(.866,-.0005,.065),(.00025,.020,.121),'expired_medicine',cardboard,0)
box('md_bottom_fold',(.9,-.0113,.003),(.064,.0003,.0007),'expired_medicine',cardboard,0)

def export(model,objs,origin):
    positions=[];normals=[];uvs=[];lookup={};buckets={}
    bpy.context.view_layer.update()
    for obj in objs:
        data=obj.data;data.calc_loop_triangles();uv=data.uv_layers.active.data
        mat=data.materials[0];bucket=buckets.setdefault(mat.name,{'mat':mat,'triangles':[]})
        for tri in data.loop_triangles:
            ids=[]
            for li in tri.loops:
                point=obj.matrix_world@data.vertices[data.loops[li].vertex_index].co-Vector(origin)
                normal=(obj.matrix_world.to_3x3().inverted().transposed()@data.corner_normals[li].vector).normalized()
                p=(-point.x,point.z,-point.y);n=(-normal.x,normal.z,-normal.y);t=uv[li].uv
                key=tuple(round(v,7) for v in (*p,*n,*t))
                if key not in lookup:
                    lookup[key]=len(positions)//3;positions.extend(p);normals.extend(n);uvs.extend(t)
                ids.append(lookup[key])
            a,b,c=[Vector(positions[i*3:i*3+3]) for i in ids]
            if (b-a).cross(c-a).dot(Vector(normals[ids[0]*3:ids[0]*3+3]))<0:ids[1],ids[2]=ids[2],ids[1]
            bucket['triangles'].extend(ids)
    subs=[]
    for name,bucket in buckets.items():
        mat=bucket['mat'];sub={'material':name,'color':list(mat['export_color']),
            'metallic':mat['export_metallic'],'smoothness':mat['export_smoothness'],'triangles':bucket['triangles']}
        if 'export_texture' in mat:sub['texture']=mat['export_texture']
        subs.append(sub)
    result={'name':model,'positions':[round(v,7) for v in positions],'normals':[round(v,6) for v in normals],
        'uvs':[round(v,6) for v in uvs],'submeshes':subs}
    (OUT/(model+'.json')).write_text(json.dumps(result,separators=(',',':')),encoding='utf-8')
    return {'triangles':sum(len(s['triangles'])//3 for s in subs),'vertices':len(positions)//3,'submeshes':len(subs)}

report={model:export(model,objs,{'beer_can':(0,0,0),'cigarette_pack':(.6,0,0),'expired_medicine':(.9,0,0)}[model]) for model,objs in groups.items()}

# Standalone review scene with the exact exported geometry and material UVs.
review=bpy.data.scenes.new('Valuables Review');review['buckshot_workshop']='valuables'
bpy.context.window.scene=review
layout={'beer_can':(-.115,0),'expired_medicine':(0,.9),'cigarette_pack':(.115,.6)}
for model,objs in groups.items():
    target,origin=layout[model]
    for obj in objs:
        copy=obj.copy();copy.data=obj.data;review.collection.objects.link(copy)
        copy.location.x+=target-origin
    label=bpy.data.curves.new(model+'_caption','FONT');label.body={'beer_can':'BEER','expired_medicine':'MEDICINE','cigarette_pack':'CIGARETTES'}[model]
    label.size=.009;label.align_x='CENTER'
    text=bpy.data.objects.new(model+'_caption',label);review.collection.objects.link(text)
    text.location=(target,-.025,-.018);text.rotation_euler=(math.pi/2,0,0)
review.world=bpy.data.worlds.new('Valuables Studio');review.world.use_nodes=True
review.world.node_tree.nodes['Background'].inputs['Color'].default_value=(.12,.13,.14,1)
review.world.node_tree.nodes['Background'].inputs['Strength'].default_value=.4
cam=bpy.data.objects.new('Valuables Camera',bpy.data.cameras.new('Valuables Camera'));review.collection.objects.link(cam);review.camera=cam
cam.data.type='ORTHO';cam.data.ortho_scale=.35
target=Vector((0,0,.068));cam.location=(.14,-.65,.32);cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler()
for name,loc,power,size in [('Key',(-.3,-.4,.65),16,.6),('Fill',(.4,.2,.5),10,.7)]:
    light=bpy.data.objects.new('Valuables '+name,bpy.data.lights.new(name,'AREA'));review.collection.objects.link(light)
    light.location=loc;light.data.energy=power;light.data.size=size;light.rotation_euler=(target-light.location).to_track_quat('-Z','Y').to_euler()
review.view_settings.view_transform='Standard';review.view_settings.look='None'
review.render.engine='CYCLES';review.cycles.samples=32;review.cycles.device='CPU'
review.render.resolution_x=1400;review.render.resolution_y=850;review.render.resolution_percentage=100
review.render.filepath=str(ROOT/'art/previews/valuables_reference.png');bpy.ops.render.render(write_still=True)
scene['asset_notes']='Beer, medicine carton and dark cigarette packet based on user photographs in art/references/original. Small print approximated. Phone and legacy source geometry preserved.'
text=bpy.data.texts.new('valuables_pipeline.py');text.write((ROOT/'art/valuables_pipeline.py').read_text(encoding='utf-8'))
bpy.data.libraries.write(str(ROOT/'art/models.blend'),{scene,text},fake_user=True,compress=True)
bpy.context.window.scene=review
for area in bpy.context.screen.areas:
    if area.type=='VIEW_3D':
        area.spaces.active.region_3d.view_perspective='CAMERA';area.spaces.active.shading.type='MATERIAL'
print(json.dumps(report))
