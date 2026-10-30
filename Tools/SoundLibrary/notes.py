import json, sys, os, numpy as np
from scipy import signal
from scipy.io import wavfile
W=sys.argv[1]; SR=44100
NOTES=['C','C#','D','D#','E','F','F#','G','G#','A','A#','B']
def nn(f):
    m=69+12*np.log2(f/440); mi=int(round(m)); return f"{NOTES[mi%12]}{mi//12-1}"
res=[]
for tr in ['002','005','008','010','001','007','015','017','004','006','012','016','011','013','014','003','009']:
    sr,x=wavfile.read(os.path.join(W,f'Mushroom_4_Part_Loop_{tr}.wav')); m=x.astype(np.float32).mean(1)/32768
    hp=signal.sosfilt(signal.butter(4,350/(SR/2),'high',output='sos'),m)
    f,t,Z=signal.stft(hp,SR,nperseg=2048,noverlap=1536); mag=np.abs(Z)+1e-9
    flux=np.maximum(0,np.diff(np.log(mag),axis=1)).sum(0); flux=(flux-flux.mean())/(flux.std()+1e-9)
    fps=SR/512; pk,_=signal.find_peaks(flux,height=2.0,distance=int(fps*0.15))
    for p in pk:
        ts=p/fps; i0=int(ts*SR)
        if i0<int(0.3*SR) or i0+int(1.2*SR)>len(m): continue
        pre=hp[i0-int(0.25*SR):i0]; att=hp[i0:i0+int(0.1*SR)]; tail=hp[i0+int(0.5*SR):i0+int(1.0*SR)]
        full=m[i0:i0+int(0.1*SR)]
        e=lambda a:20*np.log10(np.sqrt(np.mean(a**2))+1e-9)
        isol=e(att)-e(pre); dec=e(att)-e(tail); hi_ratio=e(att)-e(full)
        seg=hp[i0:i0+int(0.2*SR)]*np.hanning(int(0.2*SR)); sp=np.abs(np.fft.rfft(seg,1<<16)); ff=np.fft.rfftfreq(1<<16,1/SR)
        b=(ff>300)&(ff<2600); fpk=ff[b][np.argmax(sp[b])]
        # tonal purity: peak vs median in band
        pur=20*np.log10(sp[b].max()/(np.median(sp[b])+1e-9))
        res.append(dict(tr=tr,t=round(ts,3),note=nn(fpk),f=round(float(fpk),1),isol=round(isol,1),decay=round(dec,1),hi=round(hi_ratio,1),pur=round(pur,1),att=round(e(att),1)))
res=[r for r in res if r['att']>-34]
bell=sorted([r for r in res if r['decay']<14 and r['isol']>10], key=lambda r:-(r['isol']+r['pur']*0.6-abs(r['decay']-8)))[:12]
pluck=sorted([r for r in res if r['decay']>=14 and r['isol']>10], key=lambda r:-(r['isol']+r['pur']*0.6+r['decay']*0.2))[:12]
print("BELL candidates (long tail):"); [print(' ',r) for r in bell]
print("PLUCK candidates (fast decay):"); [print(' ',r) for r in pluck]
json.dump(dict(bell=bell,pluck=pluck),open(os.path.join(os.path.dirname(W),'note_cands.json'),'w'),indent=1)
