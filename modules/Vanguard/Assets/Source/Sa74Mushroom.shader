Shader "Boscali/SA74Mushroom"
{
    Properties
    {
        [HideInInspector] _Sa74Noise ("Billow noise", 3D) = "white" {}
        [HideInInspector] _Sa74Color ("Volume color", 2D) = "black" {}
        [HideInInspector] _Sa74Depth ("Volume scene depth", 2D) = "white" {}
        [HideInInspector] _Sa74ShockRadius ("Pressure front radius", Float) = 75000
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" }
        HLSLINCLUDE
        #include "UnityCG.cginc"
        sampler3D _Sa74Noise;
        sampler2D _CameraDepthTexture, _Sa74Color, _Sa74Depth;
        float4x4 _Sa74Frustum;
        float3 _Sa74Camera, _Sa74Sun, _Sa74SunColor, _Sa74Fog;
        float4 _Sa74Sites[2], _Sa74Size;
        float _Sa74ShockRadius;
        int _Sa74Count;
        struct Varyings { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };
        Varyings Vert(uint id:SV_VertexID)
        {
            Varyings o;
            o.uv = float2((id << 1) & 2, id & 2);
            o.pos = float4(o.uv * 2.0 - 1.0, 0.0, 1.0);
            return o;
        }
        float Growth(float age) { return saturate(1.0-exp(-max(age,0.0)/72.0)); }
        float Dispersion(float age) { return smoothstep(420.0,600.0,age); }
        float FlashPulse(float age)
        {
            // Keep the source's two light peaks paired with Domain/Sa74Flash.Pulse.
            float prompt=exp(-max(age,0.0)/.06);
            float thermal=.86*smoothstep(.22,1.1,age)*exp(-max(0.0,age-1.1)/8.5);
            return max(prompt,thermal);
        }
        float CapRadius(float age, float grow)
        {
            // Rise precedes lateral spreading: the young head stays round before the mature anvil develops.
            float spread=lerp(.42,1.0,smoothstep(18.0,170.0,age));
            return lerp(6000.0,48000.0,grow*spread);
        }
        static float gBroadLod=0.0;
        static float gSmallLod=0.0;
        float SceneEye(float2 uv)
        {
            float raw=tex2D(_CameraDepthTexture,uv).r;
            #if defined(UNITY_REVERSED_Z)
                if(raw<.000001) return 250000.0;
            #else
                if(raw>.999999) return 250000.0;
            #endif
            return LinearEyeDepth(raw);
        }
        float Density(float3 p, float age)
        {
            if(age<=2.5 || age>=600.0) return 0.0;
            float disperse=Dispersion(age);
            // The stabilized head lifts into the atmosphere and spreads along the wind as its coherent mass breaks apart.
            p.xz-=float2(10000.0,3500.0)*disperse;
            p.xz/=float2(1.0+disperse*.85,1.0+disperse*.30);
            p.y-=18000.0*disperse;
            float grow = Growth(age);
            float height = lerp(8000.0, 58000.0, grow);
            float radius = CapRadius(age,grow);
            float mature=smoothstep(20.0,180.0,age);
            float capY = height * .755, capDepth = height * lerp(.265,.24,mature);
            float radial = length(p.xz);
            float3 flow = p;
            // Roll the noise through the head's cross-section; entrained material rises centrally and curls around the rim.
            float rollAngle=age*.011;
            float rc=cos(rollAngle), rs=sin(rollAngle);
            float2 crossSection=float2(radial-radius*.60,(p.y-capY)*1.45);
            float rolledRadius=crossSection.x*rc-crossSection.y*rs+radius*.60;
            float rolledHeight=(crossSection.x*rs+crossSection.y*rc)/1.45+capY;
            float head=smoothstep(height*.45,height*.68,p.y);
            flow.xz=lerp(p.xz,p.xz/max(radial,1.0)*rolledRadius,head*.68);
            flow.y=lerp(p.y-age*100.0,rolledHeight,head);
            float twist = .23 * sin(p.y / 7000.0) + age * .0017;
            float c = cos(twist), s = sin(twist);
            flow.xz = float2(flow.x*c-flow.z*s, flow.x*s+flow.z*c);
            float4 broad = tex3Dlod(_Sa74Noise, float4(flow / 44000.0, gBroadLod));
            float4 small = tex3Dlod(_Sa74Noise, float4(flow / 13000.0 + .217, gSmallLod));
            float billow = broad.b * .58 + small.b * .42;
            float erosion = broad.g * .35 + small.a * .65;
            float3 q = float3(p.x / radius, (p.y-capY) / capDepth, p.z / radius);
            float cap = (1.0-dot(q,q)) * .95;
            float3 crownQ=float3(p.x/(radius*.61),(p.y-height*.80)/(height*.215),p.z/(radius*.61));
            float crown=(1.0-dot(crownQ,crownQ))*.90;
            float2 roll = float2((radial-radius*.64)/(radius*.30),(p.y-(capY-capDepth*.12))/(height*.185));
            float torus = 1.0-dot(roll,roll);
            float y = saturate(p.y / max(capY, 1.0));
            float stemRadius = lerp(1400.0, 6000.0, grow) * (.93 + pow(abs(2.0*y-1.0),1.5)*.28 + pow(y,4.0)*.90);
            float2 sway = float2(sin(p.y/6500.0+age*.006), cos(p.y/9500.0-age*.004)) * grow * 1100.0;
            float stem = (1.0-length(p.xz-sway)/stemRadius) * 1.5;
            stem = min(stem, (p.y+50.0)/1000.0);
            stem = min(stem, (capY+capDepth*.6-p.y)/1800.0);
            float liftOff=smoothstep(90.0,180.0,age)*(1.0-smoothstep(3000.0,11000.0,p.y));
            stem=lerp(stem,-2.0,max(liftOff,smoothstep(300.0,450.0,age)));
            float baseSkirt = (1.0-length(float3(p.x/lerp(3500.0,18000.0,grow),p.y/2200.0,p.z/lerp(3500.0,18000.0,grow)))) * .7;
            baseSkirt=lerp(baseSkirt,-2.0,smoothstep(60.0,150.0,age));
            float envelope = max(max(max(cap,crown),torus*.90),max(stem,baseSkirt));
            // Large coherent billows; fine erosion belongs at the boundary rather than perforating the whole head.
            float shape=saturate(envelope+(billow-.48)*1.05);
            float density=saturate(shape*1.65-erosion*.22*(1.0-shape)*(1.0-shape));
            float fragments=smoothstep(disperse*.82,disperse*.82+.18,broad.r*.65+small.r*.35);
            density*=lerp(1.0,fragments,disperse);
            density *= smoothstep(-80.0,80.0,p.y);
            return density * smoothstep(2.5,28.0,age);
        }
        float4 Fireball(float3 p, float age)
        {
            if(age>=65.0) return 0.0;
            float cooling=1.0-smoothstep(30.0,65.0,age);
            float radius=5000.0*(1.0-exp(-(max(age,0.0)+.08)/1.6));
            float lift=radius*.35+max(age-1.0,0.0)*140.0;
            float cloudHeight=lerp(8000.0,58000.0,Growth(age));
            lift=lerp(lift,cloudHeight*.63,smoothstep(8.0,34.0,age));
            float3 relative=p-float3(0.0,lift,0.0);
            float q=length(relative)/max(radius,1.0);
            if(q>1.45 || p.y< -150.0) return 0.0;
            float noise=tex3Dlod(_Sa74Noise,float4((relative+float3(age*32.0,-age*44.0,0.0))/7200.0,max(0.0,gSmallLod-1.0))).r;
            float core=saturate((1.0-q+(noise-.5)*.075)*12.0);
            float halo=exp(-max(q-.94,0.0)*14.0)*.055;
            float density=(core*3.0+halo)*cooling*smoothstep(-150.0,80.0,p.y);
            float young=1.0-smoothstep(6.0,24.0,age);
            float edge=smoothstep(.45,1.18,q);
            float3 surface=lerp(float3(1.0,.25,.035),float3(1.0,.86,.58),young);
            float3 color=lerp(float3(1.0,.98,.90),surface,edge*.85);
            // Normalize by the second pulse's tail: the existing post-1.1-second art stays intact while the source itself dips.
            float thermalTail=.86*exp(-max(0.0,age-1.1)/8.5);
            float sourcePulse=FlashPulse(age)/max(thermalTail,.0001);
            float brilliance=(64.0*exp(-age/12.0)+3.0)*cooling*sourcePulse;
            return float4(color*brilliance,density);
        }
        float DustFront(float age) { return min(max(75000.0,_Sa74ShockRadius),2100.0+340.0*max(age,0.0)); }
        float2 GroundDust(float3 p, float age)
        {
            if(age<=6.0 || age>=360.0 || p.y>2600.0 || p.y< -1500.0) return 0.0;
            float radial=length(p.xz), front=DustFront(age);
            if(radial>front+4000.0) return 0.0;
            float arrival=age-max(0.0,(radial-2100.0)/340.0);
            float fade=smoothstep(6.0,12.0,age)*(1.0-smoothstep(240.0,360.0,age));
            float2 direction=p.xz/max(radial,1.0);
            float3 flow=float3(p.x-direction.x*age*48.0,p.y*2.2-age*21.0,p.z-direction.y*age*48.0);
            float4 coarse=tex3Dlod(_Sa74Noise,float4(flow/18000.0,max(0.0,gSmallLod-1.0)));
            float4 fine=tex3Dlod(_Sa74Noise,float4(flow/4500.0+.31,max(0.0,gSmallLod)));
            float billow=coarse.b*.62+fine.b*.38;
            float height=850.0+billow*1350.0;
            float ground=smoothstep(-1500.0,-650.0,p.y);
            float maximumAge=(max(75000.0,_Sa74ShockRadius)-2100.0)/340.0;
            float wall=exp(-pow((radial-front)/1850.0,2.0))*1.65*(1.0-smoothstep(maximumAge+8.0,maximumAge+40.0,age));
            float wake=smoothstep(-2.0,8.0,arrival)*exp(-max(arrival-30.0,0.0)/12.0)*1.05;
            float inner=smoothstep(15.0,55.0,arrival)*.035;
            float wallHeight=1.0-smoothstep(height*.42,height+300.0,p.y);
            float recent=1.0-smoothstep(30.0,75.0,arrival);
            float wakeHeight=1.0-smoothstep(lerp(80.0,height*.20,recent),lerp(350.0,height*.80,recent),p.y);
            float innerHeight=1.0-smoothstep(80.0,450.0,p.y);
            float turbulent=.65+billow*.65+fine.r*.20;
            float density=(wall*wallHeight+wake*wakeHeight+inner*innerHeight)*turbulent*ground*fade;
            return float2(density,billow);
        }
        float2 BoxHit(float3 origin, float3 direction, float3 lo, float3 hi)
        {
            float3 inv = rcp(direction + (abs(direction)<.00001) * .00001);
            float3 a=(lo-origin)*inv, b=(hi-origin)*inv;
            float3 mn=min(a,b), mx=max(a,b);
            return float2(max(max(mn.x,mn.y),mn.z),min(min(mx.x,mx.y),mx.z));
        }
        struct MarchResult { float4 color:SV_Target0; float depth:SV_Target1; };
        MarchResult March(Varyings input)
        {
            MarchResult result;
            float2 uv=input.uv;
            float3 ray=lerp(lerp(_Sa74Frustum[0].xyz,_Sa74Frustum[1].xyz,uv.x),lerp(_Sa74Frustum[2].xyz,_Sa74Frustum[3].xyz,uv.x),uv.y);
            float rayLength=length(ray);
            float3 direction=ray/max(rayLength,.0001);
            float eye=SceneEye(uv);
            float sceneDistance=eye>=249999.0?250000.0:min(eye*rayLength,250000.0);
            result.depth=eye;
            float4 accumulated=0.0;
            float transmittance=1.0;
            [loop] for (int site=0;site<2;site++)
            {
                if(site>=_Sa74Count) break;
                float age=_Sa74Sites[site].w;
                if(age>=600.0) continue;
                float disperse=Dispersion(age);
                float grow=Growth(age);
                float height=lerp(8000.0,58000.0,grow), radius=CapRadius(age,grow)*1.20;
                radius=radius*(1.0+disperse*.85)+disperse*10000.0;
                if(age>6.0 && age<360.0) radius=max(radius,DustFront(age)+4000.0);
                float3 origin=_Sa74Camera-_Sa74Sites[site].xyz;
                float2 interval=BoxHit(origin,direction,float3(-radius,-1500.0,-radius),float3(radius,height*1.12+disperse*18000.0,radius));
                float begin=max(interval.x,0.0), end=min(interval.y,sceneDistance);
                if(end<=begin) continue;
                int steps=(int)clamp(ceil((end-begin)/230.0),32.0,age>=420.0?64.0:96.0);
                float stepLength=(end-begin)/steps;
                gBroadLod=max(0.0,log2(max(1.0,stepLength*64.0/44000.0)));
                gSmallLod=max(0.0,log2(max(1.0,stepLength*64.0/13000.0)));
                float jitter=frac(sin(dot(floor(uv*_Sa74Size.xy),float2(12.9898,78.233)))*43758.5453);
                float distance=begin+stepLength*(.15+jitter*.70);
                float heat=pow(1.0-smoothstep(20.0,105.0,age),2.0);
                float3 beforeColor=accumulated.rgb;
                float beforeTransmittance=transmittance;
                [loop] for(int i=0;i<96;i++)
                {
                    if(i>=steps || transmittance<.012) break;
                    float3 p=origin+direction*distance;
                    float cloudDensity=Density(p,age);
                    float4 fireball=Fireball(p,age);
                    float2 groundDust=GroundDust(p,age);
                    float dustDensity=groundDust.x*3.4;
                    float density=cloudDensity+fireball.a+dustDensity;
                    if(density>.002)
                    {
                        float optical=density*stepLength*.00072;
                        float alpha=1.0-exp(-optical);
                        float3 radiance=fireball.rgb*fireball.a;
                        if(dustDensity>.002)
                        {
                            float topLight=saturate((p.y+200.0)/2200.0);
                            float3 dirt=lerp(float3(.18,.125,.075),float3(.40,.32,.23),topLight)*(.70+groundDust.y*.55);
                            dirt*=float3(.65,.68,.72)+_Sa74SunColor*(.15+topLight*.35);
                            float warm=exp(-age/18.0)*saturate(1.0-length(p.xz)/18000.0)*.45;
                            dirt+=warm*float3(1.0,.45,.13);
                            radiance+=dirt*dustDensity;
                        }
                        if(cloudDensity>.002)
                        {
                            float shadow=Density(p+_Sa74Sun*2400.0,age)*1.7+Density(p+_Sa74Sun*8200.0,age)*3.8;
                            float light=exp(-shadow);
                            float forward=pow(saturate(dot(direction,-_Sa74Sun)),8.0);
                            float condensation=smoothstep(height*.45,height*.94,p.y)*smoothstep(18.0,100.0,age);
                            float3 albedo=lerp(float3(.49,.43,.35),float3(.92,.95,.98),condensation);
                            float3 skyAmbient=float3(.075,.085,.105)*lerp(.55,1.0,saturate(p.y/height));
                            float3 smoke=albedo*(skyAmbient+_Sa74SunColor*(.035+light*(1.05+forward*2.2)));
                            // Residual incandescence is inside the young head; the outer rolling rim becomes ordinary condensation.
                            float3 hotQ=float3(p.x/(radius*.48),(p.y-height*.605)/(height*.14),p.z/(radius*.48));
                            float interior=exp(-dot(hotQ,hotQ)*1.8);
                            float lower=exp(-max(p.y,0.0)/max(1800.0,height*.13));
                            float emission=heat*(interior*5.0+lower*2.4*exp(-age/14.0));
                            float3 hotColor=lerp(float3(2.4,.48,.09),float3(3.3,2.4,1.4),heat);
                            smoke+=emission*hotColor;
                            radiance+=smoke*cloudDensity;
                        }
                        float3 smoke=radiance/max(density,.00001);
                        smoke=lerp(smoke,_Sa74Fog,saturate(distance/180000.0)*.32);
                        accumulated.rgb+=transmittance*alpha*smoke;
                        transmittance*=1.0-alpha;
                    }
                    distance+=stepLength;
                }
                accumulated.rgb=beforeColor+(accumulated.rgb-beforeColor)*(1.0-disperse);
                transmittance=lerp(beforeTransmittance,transmittance,1.0-disperse);
            }
            accumulated.a=1.0-transmittance;
            result.color=accumulated;
            return result;
        }
        float4 Composite(Varyings input):SV_Target
        {
            float eye=SceneEye(input.uv);
            float4 sum=0.0;float weight=0.0;
            [unroll] for(int y=-1;y<=1;y+=2)
            [unroll] for(int x=-1;x<=1;x+=2)
            {
                float2 uv=input.uv+float2(x,y)*_Sa74Size.zw*.45;
                float depth=tex2D(_Sa74Depth,uv).r;
                float w=rcp(1.0+abs(depth-eye)/max(2.0,eye*.012));
                sum+=tex2D(_Sa74Color,uv)*w;weight+=w;
            }
            return sum/max(weight,.00001);
        }
        ENDHLSL
        Pass
        {
            Name "RaymarchedDensity"
            ZTest Always ZWrite Off Cull Off Blend Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment March
            ENDHLSL
        }
        Pass
        {
            Name "DepthAwareComposite"
            ZTest Always ZWrite Off Cull Off Blend One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Composite
            ENDHLSL
        }
    }
}
