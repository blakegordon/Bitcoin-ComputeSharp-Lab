# Bitcoin-ComputeSharp-Lab

Welcome to an experimental but functional GPU Bitcoin miner, built with .NET (only). It looks for winning hashes using Sergio Pedri's [ComputeSharp](https://github.com/Sergio0694/ComputeSharp) package. 

*Note: Non-ASIC-based mining on the live Bitcoin network has been obsolete for over a decade. Still, it never hurts to dream.*

## System Requirements

- Windows 10 or Windows 11 (64-bit)
- .NET 9.0+ (sources currently target .NET 10)
- DirectX 12 compatible GPU

---

## Quick Start: Offline Benchmark

The easiest way to test performance is to run the miner in offline benchmark mode. On Windows (only), open a Command Prompt (`cmd.exe`) and run:

```cmd
git clone https://github.com/blakegordon/Bitcoin-ComputeSharp-Lab.git
cd Bitcoin-ComputeSharp-Lab\Miner

dotnet build --configuration Release

cd bin\Release\net10.0

miner --benchmark 
```

**Example Output:**
```text
Mode:                          Offline Benchmark (10 seconds)
Target Difficulty:             ~133.87 Trillion (Mainnet)

Using 2 GPU device(s):
  [0] NVIDIA GeForce RTX 4090
  [1] NVIDIA TITAN V
  RTX 4090 warming up...
  TITAN V warming up...

[BENCHMARK] RTX 4090: 8.84 GH/s (90,000,000,000 hashes in 10.18s)
[BENCHMARK] TITAN V: 3.03 GH/s (32,000,000,000 hashes in 10.55s)
```

---

## Advanced: Live Integration with the Bitcoin Network

This miner is fully functional and can connect to a real `bitcoind` node to fetch live block templates, calculate valid nonces, and submit solved blocks back to the network. Testing the live protocol requires access to a running Bitcoin Core node (either synced to Mainnet/Testnet, or running locally in `-regtest` mode). 

If your node is running locally on the default port, the miner will automatically attempt to locate your `.cookie` file for zero-config authentication:

```cmd
miner
```

**Sample Live Production Output:**
```text
Connected to Bitcoin Core:     http://127.0.0.1:8332/ (using auth cookie)
  Chain:                       main
  Blocks:                      956,255
  Headers:                     956,255
  Difficulty:                  133.87 trillion times harder than in 2009
  Network Hashrate:            986.91 exahashes per second
  Payout address:              bc1qk80v7pux0z3vw9kjw7mx6pmkethnngs5a5q5nq (witness_v0_keyhash)

Using 2 GPU device(s):
  [0] NVIDIA GeForce RTX 4090
  [1] NVIDIA TITAN V

RTX 4090:   8.86 GH/s         TITAN V:   2.59 GH/s
RTX 4090:   8.84 GH/s         TITAN V:   2.56 GH/s
RTX 4090:   8.58 GH/s         TITAN V:   2.56 GH/s
...
```

If your node is remote, or if you are using explicit RPC credentials, pass them via command-line arguments:

```cmd
miner --rpc-url http://192.168.1.100:8332 --rpc-user alice --rpc-password bob 
```

### Tuning
The compute shader's dispatch behavior can be tuned using the `--chunk` parameter (number of nonces evaluated per GPU dispatch).

```cmd
miner --help

miner --rpc-url http://127.0.0.1:8332 --rpc-user alice --rpc-password bob --chunk 500000000
```

Enjoy!
