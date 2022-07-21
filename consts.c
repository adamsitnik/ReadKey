#include <stdio.h>
#include <termios.h>
#include <unistd.h> // for _POSIX_VDISABLE

int main(void)
{
    printf("TCSANOW=%d \n", TCSANOW);
    printf("VTIME=%d \n", VTIME);
    printf("VMIN=%d \n", VMIN);
    printf("VERASE=%d \n", VERASE);
    printf("ISIG=%d \n", ISIG);
    printf("ICANON=%d \n", ICANON);
    printf("IXON=%d \n", IXON);
    printf("IXOFF=%d \n", IXOFF);
    printf("ICRNL=%d \n", ICRNL);
    printf("INLCR=%d \n", INLCR);
    printf("IGNCR=%d \n", IGNCR);
    printf("ECHO=%d \n", ECHO);
    printf("IEXTEN=%d \n", IEXTEN);

#ifdef _POSIX_VDISABLE
    printf("_POSIX_VDISABLE=%d \n", _POSIX_VDISABLE);
#endif

    return 0;
}