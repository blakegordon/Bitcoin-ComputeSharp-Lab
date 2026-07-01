# GPU-enabled Bitcoin Miner for .NET

Welcome to my GPGPU Bitcoin miner for Windows, built with ComputeSharp and DirectX 12. It leverages HLSL compute shaders to execute unrolled double-SHA256 hashing directly on the GPU(s).

## System Requirements

- Windows 10 or Windows 11 (64-bit)
- .NET 9.0 or higher (sources currently target .NET 10)
- DirectX 12 Compatible GPU
- Access to a Bitcoin Core node

## Quick Start 

You can download, configure, and launch a lightweight local test network to benchmark the miner using a standard Windows Command Prompt (`cmd.exe`). This setup requires zero blockchain synchronization and takes up virtually no disk space.

### 1. Set Up the Workspace & Clone the Miner

Open a standard Command Prompt (`cmd.exe`) and run the following commands to create a clean workspace directory, clone the miner repository, and pull down the official Bitcoin Core binaries:

```cmd
REM 1. Create and enter a clean sandbox directory
mkdir btc-mining-test
cd btc-mining-test

REM 2. Clone this repository
git clone [https://github.com/blakegordon/BitcoinMiner.git](https://github.com/blakegordon/BitcoinMiner.git)

REM 3. Download the official Bitcoin Core archive securely using native curl
curl -L -o bitcoin.zip [https://bitcoincore.org/bin/bitcoin-core-27.1/bitcoin-27.1-win64.zip](https://bitcoincore.org/bin/bitcoin-core-27.1/bitcoin-27.1-win64.zip)

REM 4. Extract the zip folder using native tar
tar -xf bitcoin.zip

REM 5. Navigate to the executable binaries
cd bitcoin-27.1\bin
```

### 2. Start the Local Regtest Node

Launch the background node daemon process from your current directory:

```cmd
start bitcoind.exe -regtest -server -rpcuser=grok -rpcpassword=miner -rpcport=8332
```

### 3. Generate Blocks & Crank Up Difficulty

A fresh Regtest node starts at a default mining difficulty of zero. To prevent your GPU from immediately overwhelming the screen buffer with instant block solutions, copy-paste this block into your command prompt. 

It creates a temporary test wallet, fetches a payout address, and executes a 2,016-block rush with native delayed expansion enabled to aggressively tighten the target difficulty threshold:

```cmd
bitcoin-cli.exe -regtest -rpcuser=grok -rpcpassword=miner createwallet "test"

for /f "usebackq tokens=*" %a in (`bitcoin-cli.exe -regtest -rpcuser=grok -rpcpassword=miner getnewaddress`) do set ADDR=%a

echo Simulating block rush to harden target mining difficulty...

cmd /V:ON /C "for /L %i in (1,1,2016) do @(set /a MOCKTIME=1718000000 + %i & bitcoin-cli.exe -regtest -rpcuser=grok -rpcpassword=miner setmocktime !MOCKTIME! >nul 2>&1 & bitcoin-cli.exe -regtest -rpcuser=grok -rpcpassword=miner generatetoaddress 1 %ADDR% >nul)"

echo Difficulty adjusted! Node ready for baseline benchmark.
```

### 4. Launch the Miner

Now move over into your cloned miner directory and run the C# application directly against the local node:  

```cmd
REM Navigate out of the bitcoin folder and into the cloned repository
cd ..\..\BitcoinMiner

REM Build and launch the miner
dotnet run --configuration Release -- --rpc-url "[http://127.0.0.1:8332](http://127.0.0.1:8332)" --rpc-user "grok" --rpc-password "miner" --max-gpus 2
```