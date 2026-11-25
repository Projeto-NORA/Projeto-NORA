from PIL import Image, ImageFilter, ImageDraw
import numpy as np, os
src=Image.open('logo_render.png').convert('RGB'); a=np.asarray(src).astype(int)
r,g,b=a[...,0],a[...,1],a[...,2]
# background colour = median of corners
bg=np.median(np.concatenate([a[:20,:20].reshape(-1,3),a[-20:,-20:].reshape(-1,3)]),axis=0).astype(int)
print('bg',bg)
white=(r>200)&(g>200)&(b>200)
yellow=(r>200)&(g>140)&(b<120)
ys,xs=np.where(white|yellow); print('bbox',xs.min(),xs.max(),ys.min(),ys.max())
yc=np.where(yellow); print('yellow', np.median(a[yc],axis=0))
# soft alpha: distance from bg
dist=np.sqrt(((a-bg)**2).sum(-1)); alpha=np.clip(dist/ (np.sqrt(((np.array([255,255,255])-bg)**2).sum())*0.9),0,1)
ycol=np.array([249,184,0]) if not len(yc[0]) else np.median(a[yc],axis=0).astype(int)
# yellow-ness weight
yw=np.clip((r-b-60)/120.0,0,1)*(alpha>0)
x0,x1,y0,y1=xs.min(),xs.max(),ys.min(),ys.max()
def crop(arr): return arr[y0:y1+1,x0:x1+1]
A=crop(alpha); Y=crop(yw)
H,W=A.shape
def glyph(ncolor,path,size=None):
    out=np.zeros((H,W,4),np.uint8)
    col=np.array(ncolor)[None,None,:]*(1-Y[...,None])+ycol[None,None,:]*Y[...,None]
    out[...,:3]=col.astype(np.uint8); out[...,3]=(A*255).astype(np.uint8)
    im=Image.fromarray(out,'RGBA')
    # pad to square
    s=max(W,H); sq=Image.new('RGBA',(s,s),(0,0,0,0)); sq.paste(im,((s-W)//2,(s-H)//2)); 
    if size: sq=sq.resize((size,size),Image.LANCZOS)
    sq.save(path); return sq
os.makedirs('out',exist_ok=True)
NAVY=(2,16,41) if bg.sum()<10 else tuple(int(v) for v in bg)
print('navy',NAVY)
gl_dark=glyph((255,255,255),'out/nora_n_white.png',1024)   # for dark theme
gl_light=glyph(NAVY,'out/nora_n_navy.png',1024)            # for light theme
def icon(size,rounded=True,pad=0.18,path=None):
    im=Image.new('RGBA',(size,size),(0,0,0,0)); d=ImageDraw.Draw(im)
    if rounded: d.rounded_rectangle([0,0,size-1,size-1],radius=int(size*0.22),fill=NAVY+(255,))
    else: d.rectangle([0,0,size,size],fill=NAVY+(255,))
    g=gl_dark.resize((int(size*(1-2*pad)),)*2,Image.LANCZOS); im.alpha_composite(g,(int(size*pad),int(size*pad)))
    if path: im.save(path)
    return im
icon(1024,path='out/nora_icon_1024.png'); icon(512,rounded=False,path='out/play_store_icon_512.png',pad=0.2)
big=icon(256)
big.save('out/nora.ico',sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])
# Android legacy + round + adaptive foreground
dens={'mdpi':48,'hdpi':72,'xhdpi':96,'xxhdpi':144,'xxxhdpi':192}
for k,s in dens.items():
    os.makedirs(f'out/mipmap-{k}',exist_ok=True)
    icon(s,path=f'out/mipmap-{k}/ic_launcher.png')
    c=icon(s,rounded=False); m=Image.new('L',(s,s),0); ImageDraw.Draw(m).ellipse([0,0,s-1,s-1],fill=255); c.putalpha(m); c.save(f'out/mipmap-{k}/ic_launcher_round.png')
    fs=int(s*108/48); fg=Image.new('RGBA',(fs,fs),(0,0,0,0)); gs=int(fs*0.42); fg.alpha_composite(gl_dark.resize((gs,gs),Image.LANCZOS),((fs-gs)//2,(fs-gs)//2)); fg.save(f'out/mipmap-{k}/ic_launcher_foreground.png')
    # monochrome for themed icons
    mono=fg.copy(); arr=np.asarray(mono).copy(); arr[...,:3]=255; Image.fromarray(arr).save(f'out/mipmap-{k}/ic_launcher_monochrome.png')
print('ok',NAVY)
