"""Offline checks for the one-material injector and its future animation markers."""
import json
import math
import struct
from pathlib import Path

root=Path(__file__).resolve().parents[1]
data=json.loads((root/'assets/models/adrenaline.json').read_text(encoding='utf-8'))
p=list(zip(*[iter(data['positions'])]*3));n=list(zip(*[iter(data['normals'])]*3))
assert len(p)==len(n)==len(data['uvs'])//2
assert all(math.isfinite(v) for key in ('positions','normals','uvs') for v in data[key])
assert all(abs(sum(v*v for v in normal)-1)<.001 for normal in n)
assert all(0<=v<=1 for v in data['uvs'])
assert len(data['submeshes'])==1
sub=data['submeshes'][0];ids=sub['triangles']
assert ids and len(ids)%3==0 and min(ids)>=0 and max(ids)<len(p)
assert sub['texture']=='adrenaline.png' and sub['color']==[1,1,1]
blob=(root/'assets/models'/sub['texture']).read_bytes()
assert blob[:8]==b'\x89PNG\r\n\x1a\n' and struct.unpack('>II',blob[16:24])==(64,64)
for start in range(0,len(ids),3):
    i,j,k=ids[start:start+3]
    ab=[p[j][d]-p[i][d] for d in range(3)];ac=[p[k][d]-p[i][d] for d in range(3)]
    cross=[ab[1]*ac[2]-ab[2]*ac[1],ab[2]*ac[0]-ab[0]*ac[2],ab[0]*ac[1]-ab[1]*ac[0]]
    assert sum(v*v for v in cross)>1e-18,('degenerate',i,j,k)
    assert sum(cross[d]*n[i][d] for d in range(3))>0,('reversed',i,j,k)
low=[min(point[i] for point in p) for i in range(3)]
high=[max(point[i] for point in p) for i in range(3)]
size=[hi-lo for lo,hi in zip(low,high)]
assert .20<size[2]<.25 and .04<size[0]<.06 and .025<size[1]<.04,size
assert abs(low[0]+high[0])<.0001
assert set(data['markers'])=={'tip','grip','actuator'}
for marker in data['markers'].values():
    assert len(marker)==3 and all(low[i]<=marker[i]<=high[i] for i in range(3))
assert data['markers']['tip'][2]>data['markers']['grip'][2]>data['markers']['actuator'][2]
print({'triangles':len(ids)//3,'vertices':len(p),'materials':1,'texture':'64x64',
    'dimensions_m':[round(v,4) for v in size],'markers':sorted(data['markers'])})
