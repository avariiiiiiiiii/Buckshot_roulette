"""Validate the exported assets against ModelLoader's format and metre scale."""
import json
import math
import struct
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
for name in ('beer_can','expired_medicine','cigarette_pack'):
    data=json.loads((ROOT/'assets/models'/f'{name}.json').read_text(encoding='utf-8'))
    p=list(zip(*[iter(data['positions'])]*3));n=list(zip(*[iter(data['normals'])]*3))
    assert len(p)==len(n)==len(data['uvs'])//2
    assert all(math.isfinite(v) for key in ('positions','normals','uvs') for v in data[key])
    assert all(abs(sum(v*v for v in normal)-1)<.001 for normal in n)
    assert all(0<=v<=1 for v in data['uvs'])
    low=[min(point[i] for point in p) for i in range(3)]
    high=[max(point[i] for point in p) for i in range(3)]
    size=[hi-lo for lo,hi in zip(low,high)]
    assert max(size)<.20 and min(size)>.015, (name,size)
    assert abs((low[0]+high[0])/2)<.001 and abs((low[2]+high[2])/2)<.005
    assert abs(low[1])<.001, (name,'base below floor',low)
    count=0;textures=set()
    for sub in data['submeshes']:
        assert len(sub['color'])==3 and all(0<=v<=1 for v in sub['color'])
        assert 0<=sub['metallic']<=1 and 0<=sub['smoothness']<=1
        if 'texture' in sub:
            path=ROOT/'assets/models'/sub['texture'];blob=path.read_bytes()
            assert blob[:8]==b'\x89PNG\r\n\x1a\n'
            assert struct.unpack('>II',blob[16:24])==(128,128)
            textures.add(path.name)
        ids=sub['triangles'];assert len(ids)%3==0 and ids
        assert min(ids)>=0 and max(ids)<len(p)
        for a in range(0,len(ids),3):
            i,j,k=ids[a:a+3]
            ab=[p[j][d]-p[i][d] for d in range(3)]
            ac=[p[k][d]-p[i][d] for d in range(3)]
            cross=[ab[1]*ac[2]-ab[2]*ac[1],ab[2]*ac[0]-ab[0]*ac[2],ab[0]*ac[1]-ab[1]*ac[0]]
            assert sum(v*v for v in cross)>1e-18,(name,'degenerate',i,j,k)
            assert sum(cross[d]*n[i][d] for d in range(3))>0,(name,'backface',i,j,k)
        count+=len(ids)//3
    assert textures=={name+'.png'},(name,'missing printed material')
    if name!='beer_can':assert size[2]<size[0]*.5,'Carton must be shallow'
    print(name,{'triangles':count,'vertices':len(p),'materials':len(data['submeshes']),
        'dimensions_m':[round(v,4) for v in size],'textures':sorted(textures)})
