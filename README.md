# QSYSPagingAPI
A sample project that demos the Q-Sys Paging JSON-RPC API

The attached Q-Sys design has the required components.

1) publish the attached design to your core
2) Go to Administrator on that design, Audio Files, Messages and add some pre-recorded audio
3) In the project (MainWondow.cs) change the line: new TcpClient("<ip address>", xxx) to your core's IP address
4) At the Rpc.Send.. PageSubmit line: change the Message to the name of your uploaded pre-recorded audio
5) run the program
