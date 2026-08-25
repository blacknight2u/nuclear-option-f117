from pathlib import Path


source_path = Path(__file__).with_name("render_production_views.py")
source = source_path.read_text(encoding="utf-8").replace("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE")
exec(compile(source, str(source_path), "exec"))
