# NIR

A compiler infrastructure framework that utilizes a intermediate representation for the purpose of optimization and register allocation.

*Note: this is an old project and is likely to be buggy, but has some potential.*

# Features
- **Greedy register allocation**
- **Conversion to/out of SSA form**
- **Optimization passes:**
  - _Phi reduction_
  - _Constant propagation_
  - _Dead code elimination_
  - _Redundancy elimination_
- **Backends for:**
  - _x64_ __(floating-point capacity unimplemented as of Nov 2022)__