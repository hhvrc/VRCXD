// Compiles the server's websocket contract (server/Common/Protos/vrcx_live.proto) with a pure Rust
// protobuf compiler, so building the agent needs no protoc.
fn main() -> Result<(), Box<dyn std::error::Error>> {
    let root = "../server/Common/Protos";
    let proto = format!("{root}/vrcx_live.proto");
    println!("cargo:rerun-if-changed={proto}");
    let files = protox::compile([&proto], [root])?;
    prost_build::Config::new().compile_fds(files)?;
    copy_openvr()?;
    Ok(())
}

// The agent loads OpenVR from beside its exe, as the original shipped Dotnet/libs/openvr_api.dll,
// so a build puts the library there. OUT_DIR is target/<profile>/build/<crate>/out.
fn copy_openvr() -> std::io::Result<()> {
    let name = if std::env::var("CARGO_CFG_WINDOWS").is_ok() { "openvr_api.dll" } else { "libopenvr_api.so" };
    let source = std::path::Path::new("assets").join(name);
    println!("cargo:rerun-if-changed={}", source.display());
    let out = std::path::PathBuf::from(std::env::var("OUT_DIR").expect("cargo sets OUT_DIR"));
    if let Some(profile_dir) = out.ancestors().nth(3) {
        std::fs::copy(&source, profile_dir.join(name))?;
    }
    Ok(())
}
