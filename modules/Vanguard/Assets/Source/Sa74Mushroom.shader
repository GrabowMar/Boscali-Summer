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
        sampler2D _BoscaliCloudCover, _BoscaliCloudLayerDepth, _Sa74Macro;
        float4 _Sa74Char, _Sa74CharSize;
        float _Sa74CharMode;
        float4x4 _Sa74Frustum;
        float3 _Sa74Camera, _Sa74Sun, _Sa74SunColor, _Sa74Fog;
        float4 _Sa74Sites[2], _Sa74Size, _Sa74Clearance, _Sa74Wet;
        float4 _Sa74Winds[2];
        float _Sa74ShockRadius, _BoscaliCloudCoverFrame;
        int _Sa74Count;
        struct Varyings { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };
        Varyings Vert(uint id:SV_VertexID)
        {
            Varyings o;
            o.uv = float2((id << 1) & 2, id & 2);
            o.pos = float4(o.uv * 2.0 - 1.0, 0.0, 1.0);
            return o;
        }
        // The cap holds for 25 minutes, then sinks and spreads into fallout until it is gone at 40 minutes.
        #define SA74_HOLD 1500.0
        #define SA74_GONE 2400.0
        // Paired with Domain/Sa74MushroomShape.Growth: the majestic rise takes minutes, not the old 72 s sprint.
        float Growth(float age) { return saturate(1.0-exp(-max(age,0.0)/110.0)); }
        float CloudHeight(float age, float grow)
        {
            // The young head is entrained through the fireball, then develops its full towering stem by 95 s.
            return lerp(8000.0,58000.0,grow)*lerp(.50,1.0,smoothstep(18.0,95.0,age));
        }
        float Dispersion(float age) { return smoothstep(SA74_HOLD,SA74_GONE,age); }
        // Glasstone & Dolan thermal pulse scaling for 74 Mt: minimum near 0.0025*sqrt(kt) = 0.7 s,
        // second maximum near 0.0417*kt^0.44 = 5.8 s, most of the heat delivered within a minute.
        float ThermalTail(float age) { return .86*exp(-max(0.0,age-5.8)/12.0); }
        float FlashPulse(float age)
        {
            // Keep the source's two light peaks paired with Domain/Sa74Flash.Pulse.
            float prompt=exp(-max(age,0.0)/.06);
            float thermal=ThermalTail(age)*lerp(.04,1.0,smoothstep(.45,5.8,age));
            return max(prompt,thermal);
        }
        float CapRadius(float age, float grow)
        {
            // Rise precedes lateral spreading: the young head stays round before the mature anvil develops.
            float spread=lerp(.42,1.0,smoothstep(25.0,240.0,age));
            return lerp(6000.0,48000.0,grow*spread)*lerp(.66,1.0,smoothstep(12.0,65.0,age));
        }
        static float gBroadLod=0.0;
        static float gSmallLod=0.0;
        static float gFineLod=9.0;
        static float gGroundClearance=0.0;
        static float gWet=0.0;
        static float gFireLift=0.0;
        // Live wind of the site being marched, and whether this view is too distant to resolve shadows.
        static float2 gWind=float2(0.0,0.0);
        static bool gFarView=false;
        // Written by the last Density call: share of ice veil and fallout ash at the point, and the crack noise.
        static float gIce=0.0;
        static float gAsh=0.0;
        static float gCrack=.5;
        static float gDetail=0.0;
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
            gIce=0.0; gAsh=0.0; gCrack=.5;
            if(age<=2.5 || age>=SA74_GONE) return 0.0;
            float worldY=p.y;
            float disperse=Dispersion(age);
            float grow = Growth(age);
            float height = CloudHeight(age,grow);
            // Paired with Domain/Sa74MushroomShape: the standing cloud rides the live wind, sheared by height,
            // and the fallout stage smears downwind along the same heading; a dead calm keeps the historical one.
            float shear=.30+.70*saturate((p.y+gGroundClearance)/max(height,1.0));
            p.xz-=gWind*age*shear;
            float windRun=length(gWind);
            float2 heading=windRun<.001?float2(.94209,.33633):gWind/windRun;
            // Fallout stage: the stabilized head sinks toward the weather deck and smears downwind as it breaks apart.
            p.xz-=heading*26000.0*disperse;
            p.xz/=float2(1.0+disperse*1.6,1.0+disperse*.9);
            p.y+=16000.0*disperse;
            float radius = CapRadius(age,grow);
            float mature=smoothstep(28.0,240.0,age);
            float capY = height * .755, capDepth = height * lerp(.265,.24,mature);
            float radial = length(p.xz);
            float3 flow = p;
            // Roll the noise through the head's cross-section; entrained material rises centrally and curls around the rim.
            float rollAngle=age*.007;
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
            gCrack=small.r;
            float billow = broad.b * .58 + small.b * .42;
            float erosion = broad.g * .35 + small.a * .65;
            float3 q = float3(p.x / radius, (p.y-capY) / capDepth, p.z / radius);
            float cap = (1.0-dot(q,q)) * .95;
            float3 crownQ=float3(p.x/(radius*.61),(p.y-height*.80)/(height*.215),p.z/(radius*.61));
            float crown=(1.0-dot(crownQ,crownQ))*.90;
            float2 roll = float2((radial-radius*.64)/(radius*.30),(p.y-(capY-capDepth*.12))/(height*.185));
            float torus = 1.0-dot(roll,roll);
            float y = saturate((p.y+gGroundClearance) / max(capY+gGroundClearance, 1.0));
            // A multi-megaton stem is a broad column, about a quarter of the cap's width, flaring into the cap and the base.
            float stemRadius = lerp(1800.0, 11500.0, grow) * (.93 + pow(abs(2.0*y-1.0),1.5)*.28 + pow(y,4.0)*.90);
            float detached=smoothstep(110.0,240.0,age);
            float entrainedHeight=p.y+gGroundClearance+(billow-.5)*13000.0;
            stemRadius*=lerp(1.0,.55+.45*smoothstep(4500.0,18000.0,entrainedHeight),detached);
            float2 sway = float2(sin(p.y/6500.0+age*.006), cos(p.y/9500.0-age*.004)) * grow * 1100.0;
            float stem = (1.0-length(p.xz-sway)/stemRadius) * 1.5;
            stem = min(stem, (p.y+gGroundClearance+50.0)/1000.0);
            stem = min(stem, (capY+capDepth*.6-p.y)/1800.0);
            // The column of a multi-megaton burst stays rooted while its base thins; only the lowest few hundred metres clear.
            float liftOff=detached*(1.0-smoothstep(300.0,1500.0,entrainedHeight))*.5;
            stem=lerp(stem,-2.0,max(liftOff,smoothstep(900.0,1400.0,age)));
            float baseSkirt = (1.0-length(float3(p.x/lerp(3500.0,18000.0,grow),(p.y+gGroundClearance)/2200.0,p.z/lerp(3500.0,18000.0,grow)))) * .7;
            baseSkirt=lerp(baseSkirt,-2.0,smoothstep(60.0,150.0,age));
            float envelope = max(max(max(cap,crown),torus*.90),max(stem,baseSkirt));
            // Large coherent billows; fine erosion belongs at the boundary rather than perforating the whole head.
            float shape=saturate(envelope+(billow-.48)*1.05);
            if(gDetail>.5 && gFineLod<2.6 && shape>.01 && shape<.92)
            {
                // Cauliflower detail on the visible surface only; shadow taps and distant views skip it.
                float fine=tex3Dlod(_Sa74Noise,float4(flow/4300.0+.531,gFineLod)).b;
                shape=saturate(shape+(fine-.5)*.34*(1.0-shape)*smoothstep(.01,.12,shape)*saturate(2.6-gFineLod));
            }
            float density=saturate(shape*1.65-erosion*.22*(1.0-shape)*(1.0-shape));
            float fragments=smoothstep(disperse*.82,disperse*.82+.18,broad.r*.65+small.r*.35);
            density*=lerp(1.0,fragments,disperse);
            // Smooth condensation veils: the rising head lifts humid layers past saturation. They are ice and droplets, not debris.
            float veil=0.0;
            float pileus=smoothstep(14.0,22.0,age)*(1.0-smoothstep(38.0,58.0,age));
            if(pileus>.001)
            {
                float3 pq=float3(p.x/(radius*1.18),(p.y-capY)/(capDepth*1.30),p.z/(radius*1.18));
                float lens=saturate(1.0-abs(dot(pq,pq)-.90)/.18)*smoothstep(capDepth*.25,capDepth*.70,p.y-capY);
                veil=lens*pileus*.45;
            }
            float bell=smoothstep(22.0,40.0,age)*(1.0-smoothstep(70.0,125.0,age));
            if(bell>.001)
            {
                float y0=height*.24, y1=height*.47;
                float t=saturate((p.y-y0)/(y1-y0));
                float bellRadius=radius*lerp(.80,.42,pow(t,.6));
                float sheet=saturate(1.0-abs(radial-bellRadius)/(600.0+800.0*(1.0-t)));
                sheet*=smoothstep(0.0,.40,t)*(1.0-smoothstep(.80,1.0,t))*smoothstep(.22,.62,broad.g*.5+small.g*.5);
                veil=max(veil,sheet*bell*.38);
            }
            // Fallout: debris sheets sifting out beneath the cap, reaching the surface over several minutes.
            float ash=0.0;
            float ashAmount=smoothstep(150.0,480.0,age);
            float under=capY-capDepth*.35;
            if(ashAmount>.001 && p.y<under && radial<radius*.95)
            {
                float sheets=tex3Dlod(_Sa74Noise,float4(float3(p.x,p.y*.07+age*1.5,p.z)/7000.0,gSmallLod+2.0)).r;
                float reach=lerp(.35,1.0,smoothstep(200.0,700.0,age));
                float down=(under-p.y)/max(under+gGroundClearance,1.0);
                ash=smoothstep(.40,.76,sheets)*(1.0-smoothstep(reach*.35,reach,down))
                    *(1.0-smoothstep(.60,.95,radial/radius))*smoothstep(0.0,.06,down)*ashAmount*.22;
            }
            float solid=density;
            density=max(density,max(veil,ash));
            gIce=saturate((veil-solid)/max(density,.0001));
            gAsh=saturate((ash-max(solid,veil))/max(density,.0001));
            density *= smoothstep(-80.0,80.0,worldY+gGroundClearance);
            return density * smoothstep(2.5,28.0,age);
        }
        float FireRadius(float age) { return 5000.0*(1.0-exp(-(max(age,0.0)+.08)/1.6)); }
        float FireLift(float age)
        {
            float lift=FireRadius(age)*.35*(1.0-smoothstep(500.0,4000.0,gGroundClearance))+max(age-1.0,0.0)*140.0;
            // The globe rises at the cloud's slower pace, hand in hand with Domain/Sa74MushroomShape.StemFactor.
            return lerp(lift,CloudHeight(age,Growth(age))*.63,smoothstep(12.0,60.0,age));
        }
        float3 Blackbody(float t)
        {
            // Normalized incandescence ramp: dull red, orange, yellow-white, white.
            return float3(saturate(t*3.0+.12),saturate(t*t*1.9),saturate(t*t*t*1.5));
        }
        float4 Fireball(float3 p, float age)
        {
            if(age>=95.0) return 0.0;
            float cooling=1.0-smoothstep(45.0,95.0,age);
            float radius=FireRadius(age);
            float3 relative=p-float3(0.0,gFireLift,0.0);
            // Paired with Domain/Sa74MushroomShape.TorusMorph: the globe stretches, hollows and rolls into the rising head.
            float morph=smoothstep(18.0,60.0,age);
            relative.y/=1.0+.85*morph;
            float q=length(relative)/max(radius,1.0);
            if(q>3.0 || p.y+gGroundClearance< -150.0) return 0.0;
            float ground=smoothstep(-150.0,80.0,p.y+gGroundClearance);
            // Normalize by the second pulse's tail so the globe itself dims at the minimum and swells to the second maximum.
            float sourcePulse=FlashPulse(age)/max(ThermalTail(age),.0001);
            float brilliance=(64.0*exp(-age/14.0)+3.0)*cooling*sourcePulse;
            float young=1.0-smoothstep(6.0,30.0,age);
            // Air around the globe scatters its light: blue-white while it is hottest, amber as it cools.
            float3 glowColor=lerp(float3(1.0,.62,.30),float3(.80,.88,1.0),young);
            float glow=exp(-max(q-1.0,0.0)*2.1)*.0045*lerp(.35,1.0,young);
            if(q>1.3) return float4(glowColor*brilliance,glow*cooling*ground);
            float4 noise=tex3Dlod(_Sa74Noise,float4((relative+float3(age*32.0,-age*44.0,0.0))/7200.0,max(0.0,gSmallLod-1.0)));
            float core=saturate((1.0-q+(noise.r-.5)*.075)*12.0);
            // The middle-aged fireball is a rising roll, not a shrinking ball: its axis cools while the rim keeps glowing.
            core*=1.0-.62*morph*exp(-(relative.x*relative.x+relative.z*relative.z)/(radius*radius*.20));
            // The surface cools from the limb inward and breaks into convective cells: dark crust over bright cracks.
            float crust=smoothstep(8.0,40.0,age);
            float cells=smoothstep(.25,.70,noise.b*.6+noise.a*.4);
            float temperature=lerp(1.0,.50,smoothstep(8.0,80.0,age));
            temperature-=.28*smoothstep(.45,1.10,q)*lerp(.8,1.0,crust);
            temperature-=(1.0-cells)*.45*crust*smoothstep(.20,1.0,q);
            temperature=saturate(temperature);
            float3 color=Blackbody(temperature)*temperature*temperature*(.2+.8*temperature);
            float solid=core*3.0*lerp(1.0,.55+.45*temperature,crust);
            float3 mixed=(color*solid+glowColor*glow)/max(solid+glow,.00001);
            return float4(mixed*brilliance,(solid+glow)*cooling*ground);
        }
        float FrontArrival(float distance)
        {
            float travel=max(0.0,distance-2100.0), firstLeg=12.0*1650.0;
            return min(travel,firstLeg)/1650.0+max(0.0,travel-firstLeg)/850.0;
        }
        float DustFront(float age)
        {
            return min(max(75000.0,_Sa74ShockRadius),2100.0+min(max(age,0.0),12.0)*1650.0+max(age-12.0,0.0)*850.0);
        }
        float Wilson(float3 p, float age)
        {
            // Wilson cloud: the rarefaction behind the shock chills humid air below its dew point for a few seconds.
            // It forms in the moist surface layer and in thin humid layers aloft, then evaporates as pressure recovers.
            if(age<=2.5 || age>=26.0) return 0.0;
            float y=p.y+gGroundClearance;
            if(y< -100.0 || y>9500.0) return 0.0;
            float s=length(p)/DustFront(age);
            if(s<.60 || s>1.0) return 0.0;
            float humid=exp(-max(y,0.0)/1900.0)+.85*exp(-pow((y-4600.0)/420.0,2.0))+.60*exp(-pow((y-7800.0)/380.0,2.0));
            float shell=smoothstep(.62,.80,s)*(1.0-smoothstep(.93,.995,s));
            float window=smoothstep(2.5,6.0,age)*(1.0-smoothstep(13.0,26.0,age));
            float patch=tex3Dlod(_Sa74Noise,float4(p/float3(15000.0,5000.0,15000.0)+age*.004,gBroadLod)).b;
            return shell*humid*window*smoothstep(.22,.62,patch)*lerp(.22,.42,gWet)*smoothstep(-100.0,60.0,y);
        }
        float2 GroundDust(float3 p, float age)
        {
            p.y+=gGroundClearance;
            if(age<=6.0 || age>=360.0 || p.y>2600.0 || p.y< -1500.0) return 0.0;
            float radial=length(p.xz), sphere=DustFront(age);
            if(sphere<=gGroundClearance) return 0.0;
            float front=sqrt(max(0.0,sphere*sphere-gGroundClearance*gGroundClearance));
            if(radial>front+4000.0) return 0.0;
            float arrival=age-FrontArrival(sqrt(radial*radial+gGroundClearance*gGroundClearance));
            float fade=smoothstep(6.0,12.0,age)*(1.0-smoothstep(240.0,360.0,age));
            float2 direction=p.xz/max(radial,1.0);
            float3 flow=float3(p.x-direction.x*age*48.0,p.y*2.2-age*21.0,p.z-direction.y*age*48.0);
            float4 coarse=tex3Dlod(_Sa74Noise,float4(flow/18000.0,max(0.0,gSmallLod-1.0)));
            float4 fine=tex3Dlod(_Sa74Noise,float4(flow/4500.0+.31,max(0.0,gSmallLod)));
            float billow=coarse.b*.62+fine.b*.38;
            // Over water the front lofts spray and mist: lower and far thinner than a wall of dirt.
            float height=(850.0+billow*1350.0)*lerp(1.0,.55,gWet);
            // An airburst first sweeps the surface; it does not instantly recruit a uniform disc of dirt.
            float airborne=smoothstep(500.0,4000.0,gGroundClearance);
            float recruitment=smoothstep(6.0,42.0,age-FrontArrival(gGroundClearance));
            float recruitedPatches=smoothstep(.24,.62,coarse.r*.7+fine.g*.3);
            float dustRecruitment=lerp(1.0,recruitment*(.15+.85*recruitedPatches),airborne);
            height*=lerp(1.0,.25+.75*sqrt(recruitment),airborne);
            float ground=smoothstep(-1500.0,-650.0,p.y);
            float maximumAge=FrontArrival(max(75000.0,_Sa74ShockRadius));
            float wall=exp(-pow((radial-front)/1850.0,2.0))*1.65*(1.0-smoothstep(maximumAge+8.0,maximumAge+40.0,age));
            float wake=smoothstep(-2.0,8.0,arrival)*exp(-max(arrival-30.0,0.0)/12.0)*1.05;
            float inner=smoothstep(15.0,55.0,arrival)*.035;
            float wallHeight=1.0-smoothstep(height*.42,height+300.0,p.y);
            float recent=1.0-smoothstep(30.0,75.0,arrival);
            float wakeHeight=1.0-smoothstep(lerp(80.0,height*.20,recent),lerp(350.0,height*.80,recent),p.y);
            float innerHeight=1.0-smoothstep(80.0,450.0,p.y);
            float turbulent=.65+billow*.65+fine.r*.20;
            float density=(wall*wallHeight+wake*wakeHeight+inner*innerHeight)*turbulent*ground*fade*dustRecruitment;
            return float2(density*lerp(1.0,.38,gWet),billow);
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
            // Interleaved gradient noise: the half-size composite averages it away, where a white hash left visible speckle.
            float jitter=frac(52.9829189*frac(dot(floor(uv*_Sa74Size.xy),float2(.06711056,.00583715))));
            [loop] for (int site=0;site<2;site++)
            {
                if(site>=_Sa74Count) break;
                float age=_Sa74Sites[site].w;
                if(age>=SA74_GONE) continue;
                gGroundClearance=max(0.0,_Sa74Clearance[site]);
                gWet=saturate(_Sa74Wet[site]);
                gWind=_Sa74Winds[site].xz;
                gFireLift=FireLift(age);
                float disperse=Dispersion(age);
                float grow=Growth(age);
                float height=CloudHeight(age,grow), capRadius=CapRadius(age,grow)*1.20;
                float hotRadius=max(capRadius,7500.0);
                // The tall column and the low surface layer are marched separately: one coarse box around both
                // spent most of its steps on empty air and left the cap soft.
                float radius=capRadius*(1.0+disperse*1.6)+disperse*14000.0;
                float top=height*1.12;
                if(age<95.0) { radius=max(radius,15000.0); top=max(top,gFireLift+15000.0); }
                // The standing cloud drifts downwind as it ages; the march box must travel with it.
                radius+=length(gWind)*age;
                float3 origin=_Sa74Camera-_Sa74Sites[site].xyz;
                float siteDistance=length(origin);
                // Distant spectators keep the march cheap: fewer, longer steps and no unresolvable shadow taps.
                bool farSite=siteDistance>80000.0;
                gFarView=siteDistance>60000.0;
                float2 column=BoxHit(origin,direction,float3(-radius,-gGroundClearance-1500.0,-radius),float3(radius,top,radius));
                float columnBegin=max(column.x,0.0), columnEnd=min(column.y,sceneDistance);
                bool hasColumn=columnEnd>columnBegin;
                float lowBegin=0.0, lowEnd=-1.0;
                if(age>2.5 && age<360.0)
                {
                    float lowRadius=DustFront(age)+4000.0, lowTop=(age<26.0?9500.0:2600.0)-gGroundClearance;
                    float2 low=BoxHit(origin,direction,float3(-lowRadius,-gGroundClearance-1500.0,-lowRadius),float3(lowRadius,lowTop,lowRadius));
                    lowBegin=max(low.x,0.0); lowEnd=min(low.y,sceneDistance);
                }
                float fireLight=FlashPulse(age)*(1.0-smoothstep(45.0,95.0,age));
                // The luminous globe supplies the first heat. A separately white-hot cap reads as a detached second explosion.
                float heat=pow(1.0-smoothstep(25.0,150.0,age),2.0)*smoothstep(12.0,32.0,age);
                float3 beforeColor=accumulated.rgb;
                float beforeTransmittance=transmittance;
                [loop] for(int segment=0;segment<3;segment++)
                {
                    bool fine=segment==1;
                    float begin, end;
                    if(segment==0) { begin=lowBegin; end=hasColumn?min(lowEnd,columnBegin):lowEnd; }
                    else if(segment==1) { begin=columnBegin; end=columnEnd; }
                    else { begin=max(lowBegin,columnEnd); end=hasColumn?lowEnd:-1.0; }
                    if(end<=begin || transmittance<.012) continue;
                    // The moving dust wall is about 3 km thick: long grazing rays need a finer stride while it exists.
                    float steps=fine?clamp(ceil((end-begin)/(farSite?450.0:200.0)),farSite?16.0:24.0,64.0)
                        :clamp(ceil((end-begin)/1000.0),8.0,age<120.0?48.0:20.0);
                    float stepLength=(end-begin)/steps;
                    float detail=fine?stepLength*.5:min(stepLength,1500.0);
                    gBroadLod=max(0.0,log2(max(1.0,detail*64.0/44000.0)));
                    gSmallLod=max(0.0,log2(max(1.0,detail*64.0/13000.0)));
                    gFineLod=max(0.0,log2(max(1.0,detail*64.0/4300.0)));
                    float distance=begin+stepLength*(.15+jitter*.70);
                    [loop] for(int i=0;i<128;i++)
                    {
                        if(distance>=end || transmittance<.012) break;
                        float3 p=origin+direction*distance;
                        float cloudDensity=0.0, ice=0.0, ash=0.0, crack=.5;
                        float4 fireball=0.0;
                        if(fine)
                        {
                            gDetail=1.0;
                            cloudDensity=Density(p,age);
                            gDetail=0.0;
                            ice=gIce; ash=gAsh; crack=gCrack;
                            fireball=Fireball(p,age);
                        }
                        float2 groundDust=GroundDust(p,age);
                        float dustDensity=groundDust.x*3.4;
                        float mist=Wilson(p,age);
                        float density=cloudDensity+fireball.a+dustDensity+mist;
                        // Inside solid cloud the march halves its stride, so the surface resolves at twice the detail.
                        float advance=fine && cloudDensity>.08 && ash<.5 && ice<.5?stepLength*.5:stepLength;
                        if(density>.002 || fireball.a>.0001)
                        {
                            // First visible volume point, in the same eye-space units as Weather's layer depth.
                            if(density>.01) result.depth=min(result.depth,distance/rayLength);
                            float optical=density*advance*.00072;
                            float alpha=1.0-exp(-optical);
                            float3 radiance=fireball.rgb*fireball.a;
                            float3 toFire=p-float3(0.0,gFireLift,0.0);
                            float3 fireLit=float3(1.0,.80,.52)*(fireLight*22.0/(1.0+dot(toFire,toFire)/(5200.0*5200.0)));
                            if(dustDensity>.002)
                            {
                                float topLight=saturate((p.y+gGroundClearance+200.0)/2200.0);
                                float3 dirt=lerp(float3(.30,.23,.16),float3(.62,.52,.40),topLight)*(.70+groundDust.y*.55);
                                dirt=lerp(dirt,float3(.80,.84,.88)*(.75+groundDust.y*.35),gWet);
                                dirt*=float3(.50,.54,.60)+_Sa74SunColor*(.30+topLight*.70)+fireLit;
                                float warm=exp(-age/18.0)*saturate(1.0-length(p.xz)/18000.0)*.45;
                                dirt+=warm*float3(1.0,.45,.13);
                                radiance+=dirt*dustDensity;
                            }
                            if(mist>.002)
                                radiance+=float3(.95,.97,1.0)*(float3(.26,.29,.34)+_Sa74SunColor*.46+fireLit*.22)*mist;
                            if(cloudDensity>.002)
                            {
                                // One mid-depth tap carries the old two-sample optical depth (1.7 @ 2.4 km + 3.8 @ 8.2 km);
                                // beyond 60 km a half-res shadow is unresolvable, so the far view lights the cloud flat.
                                float light=gFarView?.62:exp(-Density(p+_Sa74Sun*3800.0,age)*5.5);
                                float forward=pow(saturate(dot(direction,-_Sa74Sun)),8.0);
                                float condensation=smoothstep(height*.45,height*.94,p.y)*smoothstep(18.0,100.0,age);
                                // Nitrogen oxides formed in the shocked air tint the young debris cloud rust-brown.
                                float rust=1.0-smoothstep(50.0,260.0,age);
                                float3 albedo=lerp(lerp(float3(.49,.43,.35),float3(.52,.33,.22),rust*.8),float3(.92,.95,.98),condensation);
                                // Rolls of debris read as light and dark bands instead of one flat colour up close.
                                albedo*=.70+.55*crack;
                                albedo=lerp(albedo,float3(.97,.98,1.0),ice);
                                albedo=lerp(albedo,float3(.20,.18,.16),ash);
                                // Sky light from above plus light bounced off the surface below keeps the shaded underside readable.
                                float3 skyAmbient=float3(.105,.12,.145)*lerp(.65,1.0,saturate(p.y/height))+float3(.060,.058,.055)*saturate(1.0-p.y/height);
                                float3 smoke=albedo*(skyAmbient+_Sa74SunColor*(.06+light*(1.05+forward*2.2))+fireLit*(1.0-ash));
                                // Residual incandescence is inside the young head; the outer rolling rim becomes ordinary condensation.
                                float3 hotQ=float3(p.x/(hotRadius*.48),(p.y-height*.605)/(height*.14),p.z/(hotRadius*.48));
                                float interior=exp(-dot(hotQ,hotQ)*1.8);
                                float lower=exp(-max(p.y,0.0)/max(1800.0,height*.13));
                                // Fire shows through cracks between cooler rolls of smoke instead of as one even glow.
                                float cracks=lerp(1.0,smoothstep(.30,.62,crack)*1.8,.75);
                                float emission=heat*(interior*5.0*cracks+lower*2.4*exp(-age/14.0))*(1.0-ice)*(1.0-ash);
                                float3 hotColor=lerp(float3(2.4,.48,.09),float3(3.3,2.4,1.4),heat);
                                smoke+=emission*hotColor;
                                radiance+=smoke*cloudDensity;
                            }
                            float3 smoke=radiance/max(density,.00001);
                            // Aerial perspective: distant low parts sink into the haze, the stratospheric cap stays clear above it.
                            float haze=saturate(1.0-exp(-distance/85000.0))*.70*lerp(1.0,.35,saturate((p.y+gGroundClearance)/30000.0));
                            smoke=lerp(smoke,_Sa74Fog,haze);
                            accumulated.rgb+=transmittance*alpha*smoke;
                            transmittance*=1.0-alpha;
                        }
                        distance+=advance;
                    }
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
            // Native camera/Weather targets follow ComputeScreenPos's projection flip.
            // The two SA74 passes share their own raw fullscreen UV convention.
            float2 screenUV=input.uv;
            if(_ProjectionParams.x<0.0) screenUV.y=1.0-screenUV.y;
            float eye=SceneEye(screenUV);
            float4 sum=0.0;float weight=0.0;
            [unroll] for(int y=-1;y<=1;y+=2)
            [unroll] for(int x=-1;x<=1;x+=2)
            {
                float2 uv=input.uv+float2(x,y)*_Sa74Size.zw*.45;
                float depth=tex2D(_Sa74Depth,uv).r;
                if(depth>eye+max(2.0,eye*.012)) continue;
                // A layer in front of terrain remains visible; a low-res neighbour behind it is rejected.
                float w=rcp(1.0+max(depth-eye,0.0)/max(2.0,eye*.012));
                sum+=tex2D(_Sa74Color,uv)*w;weight+=w;
            }
            float4 volume=sum/max(weight,.00001);
            if(_BoscaliCloudCoverFrame>.5 && volume.a>.0001)
            {
                float4 weather=tex2D(_BoscaliCloudCover,screenUV);
                float cloudEye=tex2D(_BoscaliCloudLayerDepth,screenUV).g;
                float volumeEye=tex2D(_Sa74Depth,input.uv).r;
                float separation=max(400.0,min(cloudEye,volumeEye)*.025);
                float foreground=smoothstep(-separation,separation,volumeEye-cloudEye);
                // Weather is already in the framebuffer. Preserve its foreground radiance while
                // attenuating only the new layer behind it; ordinary alpha attenuation darkened it twice.
                volume.rgb=volume.rgb*(1.0-foreground*weather.a)+foreground*volume.a*weather.rgb;
            }
            return volume;
        }
        float CharNoise(float2 p)
        {
            float2 cell=floor(p), f=frac(p);
            f=f*f*(3.0-2.0*f);
            float a=frac(sin(dot(cell,float2(127.1,311.7)))*43758.5453), b=frac(sin(dot(cell+float2(1.0,0.0),float2(127.1,311.7)))*43758.5453);
            float c=frac(sin(dot(cell+float2(0.0,1.0),float2(127.1,311.7)))*43758.5453), d=frac(sin(dot(cell+float2(1.0,1.0),float2(127.1,311.7)))*43758.5453);
            return lerp(lerp(a,b,f.x),lerp(c,d,f.x),f.y);
        }
        float4 TerrainChar(Varyings input):SV_Target
        {
            // Rewrites the map's whole-terrain colour: char around the burst, breaking into burned patches at the edge.
            // Texel-exact copy: the output pixel is the source texel, whatever the target's vertical convention.
            float2 uv=input.pos.xy*_Sa74CharSize.xy;
            float4 colour=tex2D(_Sa74Macro,uv);
            float2 offset=(uv-_Sa74Char.xy)*float2(1.0,_Sa74CharSize.z);
            float radius=length(offset)/max(_Sa74Char.z,.00001);
            float noise=CharNoise(uv*90.0)*.5+CharNoise(uv*310.0)*.3+CharNoise(uv*1100.0)*.2;
            float burn=(1.0-smoothstep(.50+noise*.38,1.0,radius))*_Sa74Char.w;
            // Cover weights (fields, grass, lush) are burned away instead of recoloured, leaving the charred base ground.
            if(_Sa74CharMode>.5) return colour*(1.0-burn);
            float luminance=dot(colour.rgb,float3(.30,.59,.11));
            float3 ash=float3(.050,.046,.043)+luminance*float3(.11,.10,.09)+noise*.012;
            colour.rgb=lerp(colour.rgb,ash,burn*(.82+.18*noise));
            return colour;
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
        Pass
        {
            Name "TerrainChar"
            ZTest Always ZWrite Off Cull Off Blend Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment TerrainChar
            ENDHLSL
        }
    }
}
