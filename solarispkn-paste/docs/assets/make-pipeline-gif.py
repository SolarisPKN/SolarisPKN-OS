#!/usr/bin/env python3
"""Generate an original animated GIF89a pipeline without external libraries."""
from pathlib import Path
import struct

W,H=480,168
palette=[
    (12,22,38), (232,247,255), (46,218,175), (58,161,224),
    (242,175,70), (78,100,130), (156,118,241), (25,48,75)
]
glyphs={
"A":[14,17,17,31,17,17,17],"B":[30,17,17,30,17,17,30],
"C":[14,17,16,16,16,17,14],"D":[30,17,17,17,17,17,30],
"E":[31,16,16,30,16,16,31],"F":[31,16,16,30,16,16,16],
"G":[14,17,16,23,17,17,14],"H":[17,17,17,31,17,17,17],
"I":[31,4,4,4,4,4,31],"J":[7,2,2,2,18,18,12],
"K":[17,18,20,24,20,18,17],"L":[16,16,16,16,16,16,31],
"M":[17,27,21,21,17,17,17],"N":[17,25,21,19,17,17,17],
"O":[14,17,17,17,17,17,14],"P":[30,17,17,30,16,16,16],
"Q":[14,17,17,17,21,18,13],"R":[30,17,17,30,20,18,17],
"S":[15,16,16,14,1,1,30],"T":[31,4,4,4,4,4,4],
"U":[17,17,17,17,17,17,14],"V":[17,17,17,17,17,10,4],
"W":[17,17,17,21,21,21,10],"X":[17,17,10,4,10,17,17],
"Y":[17,17,10,4,4,4,4],"Z":[31,1,2,4,8,16,31],
"0":[14,17,19,21,25,17,14],"1":[4,12,4,4,4,4,14],
"2":[14,17,1,2,4,8,31],"3":[30,1,1,14,1,1,30],
"4":[2,6,10,18,31,2,2],"5":[31,16,16,30,1,1,30],
"6":[14,16,16,30,17,17,14],"7":[31,1,2,4,8,8,8],
"8":[14,17,17,14,17,17,14],"9":[14,17,17,15,1,1,14],
" ":[0]*7,"-":[0,0,0,31,0,0,0],":":[0,4,4,0,4,4,0],
}
def fill(buf,x,y,w,h,color):
    for yy in range(max(0,y),min(H,y+h)):
        beg=max(0,x);end=min(W,x+w)
        if end>beg:buf[yy*W+beg:yy*W+end]=bytes([color])*(end-beg)
def caption(buf,x,y,word,color=1,scale=2):
    for letter in word.upper():
        bits=glyphs.get(letter,glyphs[" "])
        for yy,mask in enumerate(bits):
            for xx in range(5):
                if mask & (1<<(4-xx)):
                    fill(buf,x+xx*scale,y+yy*scale,scale,scale,color)
        x+=6*scale
def make_frame(index):
    pixels=bytearray([0])*(W*H)
    fill(pixels,0,0,W,54,7)
    caption(pixels,20,16,"SOLARIS PASTE",1,3)
    for x,title,active in [
        (20,"READ",index>=0),(178,"BLAKE3",index>=4),
        (336,"SAVE",index>=8)
    ]:
        fill(pixels,x,73,125,64,5)
        fill(pixels,x+3,76,119,58,7)
        caption(pixels,x+13,88,title,1,2)
        fill(pixels,x+13,113,99,7,5)
        if active:
            width=min(99,max(0,(index%4+1)*25))
            fill(pixels,x+13,113,width,7,2)
    fill(pixels,146,101,30,3,3)
    fill(pixels,306,101,27,3,3)
    for x in [165,322]:
        fill(pixels,x,97,6,12,3)
    # Original animated file payload: 12 stable uniform frames.
    x=20+index*38
    fill(pixels,x,146,19,10,4)
    fill(pixels,x+3,148,13,6,1)
    return pixels

def encode_lzw_literals(pixels):
    # Emit CLEAR every 4 literal pixels. Using fixed 4-bit GIF codes and
    # frequent resets keeps the decoder's dictionary within 4-bit code size.
    codes=[8]
    for i,pixel in enumerate(pixels):
        if i>0 and i%4==0:codes.append(8)
        codes.append(pixel)
    codes.append(9)
    data=bytearray();acc=0;bits=0
    for code in codes:
        acc|=code<<bits;bits+=4
        while bits>=8:
            data.append(acc&255);acc >>=8;bits-=8
    if bits:data.append(acc&255)
    return bytes(data)

def chunk(data):
    return b"".join(bytes([len(data[i:i+255])])+data[i:i+255]
                    for i in range(0,len(data),255))+b"\x00"

def main():
    path=Path(__file__).with_name("copy-pipeline.gif")
    with path.open("wb") as out:
        out.write(b"GIF89a")
        out.write(struct.pack("<HHBBB",W,H,0xF2,0,0))
        for rgb in palette:out.write(bytes(rgb))
        out.write(b"\x21\xff\x0bNETSCAPE2.0\x03\x01\x00\x00\x00")
        for i in range(12):
            out.write(b"\x21\xf9\x04\x08")
            out.write(struct.pack("<HBB",13,0,0))
            out.write(b"\x2c"+struct.pack("<HHHHB",0,0,W,H,0))
            out.write(bytes([3])) # LZW min code size = 3, clear=8/end=9
            out.write(chunk(encode_lzw_literals(make_frame(i))))
        out.write(b"\x3b")
    print(f"GIF_OK {path} {path.stat().st_size} bytes 12 frames {W}x{H}")

if __name__=="__main__":main()
