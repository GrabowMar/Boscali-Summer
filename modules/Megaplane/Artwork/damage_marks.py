"""Author the Groza's damage decal atlas with NumPy. Units in each tile are normalized to its local patch.
Run with Blender's bundled Python. All colour, alpha and cut shapes are generated here."""
import binascii
import struct
import zlib
from pathlib import Path
import numpy as np

ASSETS = Path(__file__).resolve().parent.parent / "Assets"
SIZE = 512


def png(path, data):
    def chunk(name, content):
        return struct.pack(">I", len(content))+name+content+struct.pack(">I", binascii.crc32(name+content))
    data = np.ascontiguousarray(data, np.uint8)
    h, w, _ = data.shape
    raw = b"".join(b"\0"+row.tobytes() for row in data)
    path.write_bytes(b"\x89PNG\r\n\x1a\n"+chunk(b"IHDR",struct.pack(">2I5B",w,h,8,6,0,0,0))+
                     chunk(b"IDAT",zlib.compress(raw,9))+chunk(b"IEND",b""))


def tile(style, seed):
    rng = np.random.default_rng(seed)
    coord=(np.arange(SIZE)+.5)/SIZE
    x,y=np.meshgrid(coord,coord)
    def noise(n):
        g=rng.uniform(-1,1,(n+1,n+1))
        k=coord*n; i=k.astype(int); f=k-i; f=f*f*(3-2*f)
        a=g[i[:,None],i]*(1-f)+g[i[:,None],i+1]*f
        b=g[i[:,None]+1,i]*(1-f)+g[i[:,None]+1,i+1]*f
        return a*(1-f[:,None])+b*f[:,None]
    swirl=noise(9)*.48+noise(23)*.30+noise(58)*.16+noise(148)*.06
    fleck=noise(94)*.62+rng.uniform(-.38,.38,x.shape)
    alpha=np.zeros_like(x); color=np.zeros((*x.shape,3))
    def coat(rgb, coverage):
        nonlocal color,alpha
        a=np.clip(coverage,0,1)
        color=color*(1-a[:,:,None])+np.asarray(rgb)*a[:,:,None]
        alpha=alpha*(1-a)+a
    def ramp(d, width=.006):
        return np.clip(.5-d/width,0,1)
    def cut(cx,cy,rx,ry,angle):
        dx=x-cx; dy=y-cy; c,s=np.cos(angle),np.sin(angle)
        u=(dx*c+dy*s)/rx; v=(dy*c-dx*s)/ry
        phase=np.arctan2(v,u)
        d=np.sqrt(u*u+v*v)+.095*np.sin(5*phase+seed)+.12*np.sin(3*phase+seed*.7)+.11*fleck
        # The dirty annulus is asymmetric. Only part of the punctured circumference catches daylight.
        coat((27,26,24),np.exp(-((u-.28)**2+(v+.3)**2)*.28)*.78)
        coat((104,105,79),ramp(d-1.32,.10)*.95)
        bright=np.clip(.23+.61*np.cos(phase-2.45)+fleck*.38,0,1)
        rim=73+111*bright
        coat(np.stack([rim*.96,rim,rim*1.01],axis=2),ramp(d-1.12,.09))
        coat((14,17,19),ramp(d-.78,.08))
        coat((5,8,10),ramp(d-.45,.12)*.86)
    if style==0:
        # Irregular strike group with fine fragments and short down-flow wipes of residue.
        for cx,cy in ((.25,.30),(.39,.39),(.55,.47),(.60,.59),(.72,.68)):
            cx+=rng.uniform(-.044,.044); cy+=rng.uniform(-.045,.045)
            rx=rng.uniform(.011,.024); ry=rx*rng.uniform(.65,1.06)
            u=(x-cx)/(.053+rx); v=(y-cy-.024)/.094
            coat((42,39,35), np.exp(-2.4*(u*u+v*v))*(.66+swirl*.6))
            cut(cx,cy,rx,ry,rng.uniform(-1.3,1.2))
            for _ in range(2):
                cut(cx+rng.uniform(-.046,.05),cy+rng.uniform(-.044,.053),rx*.27,ry*.25,.9)
    elif style==1:
        # Two missing islands in the exterior sheet expose the same underlying framed cavity.
        # Their metre-scale footprints, damaged fastener rows and lip bevels read under direct sun.
        u=(x-.52-.16*(y-.5)-swirl*.12)/(.17+.13*y)
        v=(y-.51)/.41
        coat((32,32,30),np.exp(-2.4*(u*u+v*v))*(.46+.20*swirl))
        for _ in range(12):
            cx=rng.uniform(.27,.76); cy=rng.uniform(.27,.74)
            cut(cx,cy,rng.uniform(.002,.009),rng.uniform(.003,.010),rng.uniform(-1.,1.))

        def signed_polygon(px,py,loop):
            inside=np.zeros(x.shape,dtype=bool)
            distance=np.full(x.shape,10.)
            for (ax,ay),(bx,by) in zip(loop,loop[1:]+loop[:1]):
                dx=bx-ax; dy=by-ay
                k=np.clip(((px-ax)*dx+(py-ay)*dy)/(dx*dx+dy*dy),0,1)
                distance=np.minimum(distance,(px-ax-k*dx)**2+(py-ay-k*dy)**2)
                if abs(dy)>1e-10:
                    inside^=((ay>py)!=(by>py)) & (px<(py-ay)*dx/dy+ax)
            return np.sqrt(distance)*np.where(inside,-1,1)

        def opening(cx,cy,w,h,skew,hand):
            # Torn skin alternates between a row of released rivets and short diagonal rips.
            loop=[(-.51,-.30),(-.36,-.47),(-.16,-.42),(-.10,-.50),(.16,-.44),
                  (.32,-.52),(.49,-.36),(.46,-.10),(.54,-.02),(.43,.14),
                  (.47,.33),(.30,.42),(.12,.38),(-.01,.50),(-.24,.41),
                  (-.43,.52),(-.41,.29),(-.55,.22),(-.46,.02)]
            def world(s,t):
                return cx+hand*w*s+skew*h*t,cy+h*t
            px=x+swirl*.012+fleck*.0024; py=y+swirl*.008+fleck*.0016
            d=signed_polygon(px,py,[world(s,t) for s,t in loop])
            fill=ramp(d,.0035)
            s=hand*(x-cx-skew*(y-cy))/w
            t=(y-cy)/h
            dust=np.clip(.45+swirl*.65+fleck*.47,0,1)
            coat((77,80,68),ramp(d-(.006+.011*dust),.003)*(.49+.46*dust))
            # Only the lip facing daylight flashes silver. Most of the edge remains dark primer.
            lit=np.clip(.29+.43*np.sin(s*13-t*10)+.26*fleck,0,1)
            rim=66+113*lit
            coat(np.stack([rim*.99,rim,rim*.92],axis=2),ramp(d-.0045,.0026))
            depth=np.clip(-d/.032,0,1)
            deck=(21+24*depth+8*(t+.3)+swirl*14+fleck*4)
            coat(np.stack([deck*.83,deck*.94,deck*1.03],axis=2),fill)
            # Offset shadows under each hat-section flange establish the depth of the opening.
            def beam(axis,mark,web,flange):
                dist=axis-mark
                def band(a,b):
                    return ramp(abs(a)-b,.0023)*fill
                coat((12,18,22),band(dist-.012,web+.005)*.9)
                coat((72,79,76),band(dist,web))
                coat((139,149,145),band(dist+web*.35,flange))
                coat((42,48,47),band(dist-web*.60,flange*.62))
                coat((179,184,167),band(dist+web*.86,flange*.42)*(.67+.26*fleck))
            stations=(-.26,.15) if w>.30 else (.04,)
            along=(cx+hand*w*s)-.052*(y-cy)
            for station in stations:
                beam(along,cx+station*w,.009,.0026)
            # Bare spar-cap with a lighter crown and a riveted web, interrupted by the torn edges.
            rail=y+.057*hand*(x-cx)
            railpos=cy-.17*h
            beam(rail,railpos,.0125,.0038)
            rivet_x=np.abs(np.mod((x-cx)+.011,.032)-.016)
            rivets=((rivet_x/.0024)**2+((rail-railpos+.003)/.0028)**2)
            coat((175,173,155),ramp(rivets-1,.8)*fill*.85)
            # A surviving sheet tongue bends across one end; a broken tip exposes its underside.
            tabs=[(-.47,-.32),(-.22,-.46),(-.20,-.29),(-.03,-.16),(-.21,-.21),(-.37,-.15)]
            flap=signed_polygon(px,py,[world(a,b) for a,b in tabs])
            coat((16,20,21),ramp(flap-.005,.003)*fill*.88)
            bend=np.clip(1-np.abs(t+.32+(s+.32)*.32)/.14,0,1)
            metal=71+76*bend+fleck*10
            coat(np.stack([metal*.94,metal*.99,metal],axis=2),ramp(flap,.003)*fill)
            # Scattered primer chips follow the perimeter, with whole spans left painted.
            chips=ramp(abs(d-.011)-.007,.002)*np.clip((fleck+.48*swirl-.31)*2.9,0,1)
            coat((149,151,128),chips)
        if seed%2:
            opening(.60,.57,.52,.22,.34,1)
            opening(.32,.34,.21,.115,-.16,-1)
        else:
            opening(.56,.58,.47,.235,-.40,-1)
            opening(.71,.31,.23,.105,.13,1)
    else:
        # Gravel skid: parallel scoring with ragged strips of zinc primer and abraded alloy.
        for i in range(47):
            cx=rng.normal(.5,.095); cy=rng.uniform(.42,.6); length=rng.uniform(.11,.37)
            width=rng.uniform(.0014,.012)
            u=(x-cx-.115*(y-cy)-.007*swirl)/width
            v=(y-cy)/length
            d=abs(u)+abs(v)**2 +swirl*.85+fleck*.58
            coat((72,68,53),ramp(d-1.55,.25)*.94)
            coat((145,143,109),ramp(d-1.02,.21)*.9)
            coat((170,174,165),ramp(d-.63,.25)*.96)
    border=np.minimum.reduce([x,y,1-x,1-y])
    rgba=np.empty((*x.shape,4),np.uint8)
    rgba[:,:,:3]=np.uint8(np.clip(color/np.maximum(alpha[:,:,None],1e-7),0,255))
    rgba[:,:,3]=np.uint8(np.clip(alpha*255*np.clip((border-.018)/.075,0,1),0,255))
    return rgba


def main():
    atlas=np.zeros((2*SIZE,4*SIZE,4),np.uint8)
    for style in range(3):
        for variant in range(2):
            # Unity UV origin is the lower left.
            atlas[(1-variant)*SIZE:(2-variant)*SIZE,style*SIZE:(style+1)*SIZE]=tile(style,179+style*31+variant*373)
    png(ASSETS/"BS_Megaplane_DamageMarks.png",atlas)


if __name__=="__main__":
    main()
