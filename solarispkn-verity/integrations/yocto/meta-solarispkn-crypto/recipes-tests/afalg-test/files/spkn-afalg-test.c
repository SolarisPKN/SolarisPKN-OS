/* SPDX-License-Identifier: MIT */
#define _GNU_SOURCE
#include <errno.h>
#include <linux/if_alg.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/socket.h>
#include <unistd.h>
static const size_t lens[] = {0,3,64,1024,4096,1048576};
static const char *expected[] = {
    "af1349b9f5f9a1a6a0404dea36dcc9499bcb25c9adc112b7cc9a93cae41f3262",
    "6437b3ac38465133ffb63b75273a8db548c558465d79db03fd359c6cd5bd9d85",
    "3620f7f176d4df18f940500951e5f9b1ea949d5fd03c84bd722870adfdb7e827",
    "31869468f7abf02e9bbdac381d2cd933c05029e4a7e54331c81cf98b4a6279fc",
    "0360c628af501c13a16f97c29648986f2a500fb41245a552087232b6447126c3",
    "9a8e6f963082b9127ca345a2bc7fd1e58608629ee34067b452c743d6ad43ec6a",
};
static void tohex(const unsigned char *bytes,char *hex,size_t count) {
 static const char h[]="0123456789abcdef";
 for(size_t i=0;i<count;i++){hex[2*i]=h[bytes[i]>>4];hex[2*i+1]=h[bytes[i]&15];}
 hex[2*count]=0;
}
int main(void) {
 struct sockaddr_alg address={0};
 int fd,failures=0;
 address.salg_family=AF_ALG;
 strcpy((char*)address.salg_type,"hash");
 strcpy((char*)address.salg_name,"blake3");
 fd=socket(AF_ALG,SOCK_SEQPACKET,0);
 if(fd<0){perror("socket");return 2;}
 if(bind(fd,(struct sockaddr*)&address,sizeof(address))<0){perror("bind blake3");return 2;}
 for(size_t i=0;i<sizeof(lens)/sizeof(lens[0]);i++) {
   size_t n=lens[i],off=0;
   unsigned char *buf=malloc(n?n:1),digest[32];
   char hex[65];
   if(!buf){perror("malloc");return 2;}
   for(size_t k=0;k<n;k++)buf[k]=(unsigned char)((k*7+13)&255);
   if(n==3)memcpy(buf,"abc",3);
   int op=accept(fd,NULL,NULL);
   if(op<0){perror("accept");return 2;}
   if(n==0) {
     if(send(op,"",0,0)<0){perror("send zero");return 2;}
   } else {
     while(off<n) {
       size_t step=(n-off>4096)?4096:n-off;
       int flags=off+step<n?MSG_MORE:0;
       ssize_t w=send(op,buf+off,step,flags);
       if(w<=0){perror("send");return 2;}
       off+=(size_t)w;
     }
   }
   ssize_t r=read(op,digest,sizeof(digest));
   if(r!=(ssize_t)sizeof(digest)){fprintf(stderr,"digest read %zd errno %d\n",r,errno);return 2;}
   tohex(digest,hex,sizeof(digest));
   int pass=strcmp(hex,expected[i])==0;
   printf("VECTOR n=%zu %s got=%s expected=%s\n",n,pass?"PASS":"FAIL",hex,expected[i]);
   fflush(stdout);
   failures+=!pass;
   close(op);free(buf);
 }
 close(fd);
 if(failures){printf("SPKN_KERNEL_BLAKE3_TEST_FAIL n=%d\n",failures);return 1;}
 puts("SPKN_KERNEL_BLAKE3_TEST_PASS");
 return 0;
}
